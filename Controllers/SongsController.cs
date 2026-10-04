using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MusicPlatform.API.Data;
using MusicPlatform.API.Models;
using MusicPlatform.API.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MusicPlatform.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SongsController : ControllerBase
    {
        private readonly MusicDbContext _context;
        private readonly IR2StorageService _storageService;
        private readonly IMemoryCache _cache;

        private static readonly string AllSongsCacheKey = "songs_list_all";
        private static string SongMetadataCacheKey(Guid id) => $"song_metadata_{id}";
        private static string SongAudioBytesCacheKey(Guid id) => $"song_audio_bytes_{id}";
        private static string SongAudioTypeCacheKey(Guid id) => $"song_audio_type_{id}";

        public SongsController(MusicDbContext context, IR2StorageService storageService, IMemoryCache cache)
        {
            _context = context;
            _storageService = storageService;
            _cache = cache;
        }

        private Song PrepareSongForResponse(Song s)
        {
            if (string.IsNullOrWhiteSpace(s.ArtworkUri)) return s;

            string formattedUri = s.ArtworkUri;

            if (s.ArtworkUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                s.ArtworkUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                s.ArtworkUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                formattedUri = s.ArtworkUri;
            }
            else if (s.ArtworkUri.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                var req = HttpContext?.Request;
                formattedUri = (req != null && req.Host.HasValue) 
                    ? $"{req.Scheme}://{req.Host.Value}{s.ArtworkUri}"
                    : s.ArtworkUri;
            }
            else
            {
                var req = HttpContext?.Request;
                formattedUri = (req != null && req.Host.HasValue) 
                    ? $"{req.Scheme}://{req.Host.Value}/api/songs/{s.Id}/artwork"
                    : $"/api/songs/{s.Id}/artwork";
            }

            return new Song
            {
                Id = s.Id,
                Title = s.Title,
                Artist = s.Artist,
                Album = s.Album,
                FileKey = s.FileKey,
                ContentType = s.ContentType,
                Duration = s.Duration,
                FileSize = s.FileSize,
                ArtworkUri = formattedUri,
                CreatedAt = s.CreatedAt
            };
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            if (!_cache.TryGetValue(AllSongsCacheKey, out List<Song>? songs))
            {
                songs = await _context.Songs
                    .OrderByDescending(s => s.CreatedAt)
                    .ToListAsync();

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(15));

                _cache.Set(AllSongsCacheKey, songs, cacheEntryOptions);
            }

            return Ok(songs!.Select(PrepareSongForResponse));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var cacheKey = SongMetadataCacheKey(id);
            if (!_cache.TryGetValue(cacheKey, out Song? song))
            {
                song = await _context.Songs.FindAsync(id);
                if (song == null)
                {
                    return NotFound();
                }

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30));

                _cache.Set(cacheKey, song, cacheEntryOptions);
            }

            return Ok(PrepareSongForResponse(song!));
        }

        [HttpPost]
        [DisableRequestSizeLimit] // Allow large files
        public async Task<IActionResult> Upload(
            [FromForm] string title, 
            [FromForm] string artist, 
            [FromForm] string? album, 
            [FromForm] double? duration, 
            [FromForm] string? artworkUri, 
            IFormFile file,
            IFormFile? artworkFile)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No audio file provided.");
            }

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            {
                return BadRequest("Title and Artist are required fields.");
            }

            try
            {
                // 1. Upload audio file to Cloudflare R2 storage
                using var audioStream = file.OpenReadStream();
                var fileKey = await _storageService.UploadFileAsync(audioStream, file.FileName, file.ContentType);

                // 2. Handle Artwork file or Base64 image payload -> upload to Cloudflare R2 & store key only
                string? storedArtworkKey = null;

                if (artworkFile != null && artworkFile.Length > 0)
                {
                    using var artStream = artworkFile.OpenReadStream();
                    storedArtworkKey = await _storageService.UploadFileAsync(artStream, artworkFile.FileName, artworkFile.ContentType);
                }
                else if (!string.IsNullOrWhiteSpace(artworkUri) && artworkUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var commaIndex = artworkUri.IndexOf(",");
                        if (commaIndex > 0)
                        {
                            var header = artworkUri.Substring(0, commaIndex);
                            var base64Data = artworkUri.Substring(commaIndex + 1);
                            var bytes = Convert.FromBase64String(base64Data);

                            string contentType = "image/png";
                            if (header.Contains("image/jpeg") || header.Contains("image/jpg")) contentType = "image/jpeg";
                            else if (header.Contains("image/gif")) contentType = "image/gif";
                            else if (header.Contains("image/webp")) contentType = "image/webp";

                            string ext = contentType.Split('/')[1];
                            using var artStream = new MemoryStream(bytes);
                            storedArtworkKey = await _storageService.UploadFileAsync(artStream, $"artwork.{ext}", contentType);
                        }
                    }
                    catch
                    {
                        storedArtworkKey = artworkUri;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(artworkUri))
                {
                    storedArtworkKey = artworkUri;
                }

                // 3. Create Song database record with image R2 key
                var song = new Song
                {
                    Id = Guid.NewGuid(),
                    Title = title,
                    Artist = artist,
                    Album = album ?? string.Empty,
                    FileKey = fileKey,
                    ContentType = file.ContentType,
                    Duration = duration ?? 0,
                    FileSize = file.Length,
                    ArtworkUri = storedArtworkKey,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Songs.Add(song);
                await _context.SaveChangesAsync();

                // Evict list cache
                _cache.Remove(AllSongsCacheKey);

                return CreatedAtAction(nameof(GetById), new { id = song.Id }, PrepareSongForResponse(song));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("{id:guid}/stream")]
        public async Task<IActionResult> Stream(Guid id)
        {
            var audioBytesKey = SongAudioBytesCacheKey(id);
            var audioTypeKey = SongAudioTypeCacheKey(id);

            if (_cache.TryGetValue(audioBytesKey, out byte[]? cachedBytes) && 
                _cache.TryGetValue(audioTypeKey, out string? cachedType))
            {
                var cachedStream = new MemoryStream(cachedBytes!);
                return File(cachedStream, cachedType!, enableRangeProcessing: true);
            }

            // Retrieve song metadata (from cache if possible)
            var metadataKey = SongMetadataCacheKey(id);
            if (!_cache.TryGetValue(metadataKey, out Song? song))
            {
                song = await _context.Songs.FindAsync(id);
                if (song == null)
                {
                    return NotFound();
                }
                _cache.Set(metadataKey, song, TimeSpan.FromMinutes(30));
            }

            try
            {
                var (stream, contentType, contentLength) = await _storageService.GetFileStreamAsync(song!.FileKey);
                
                // Copy S3 network stream to MemoryStream to make it seekable.
                var memoryStream = new MemoryStream();
                using (stream)
                {
                    await stream.CopyToAsync(memoryStream);
                }
                var audioBytes = memoryStream.ToArray();
                memoryStream.Position = 0;

                // Cache audio bytes and content type
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromHours(2));

                _cache.Set(audioBytesKey, audioBytes, cacheEntryOptions);
                _cache.Set(audioTypeKey, contentType, cacheEntryOptions);

                return File(memoryStream, contentType, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error streaming audio: {ex.Message}");
            }
        }

        [HttpGet("{id:guid}/artwork")]
        public async Task<IActionResult> GetArtwork(Guid id)
        {
            var metadataKey = SongMetadataCacheKey(id);
            if (!_cache.TryGetValue(metadataKey, out Song? song))
            {
                song = await _context.Songs.FindAsync(id);
                if (song == null) return NotFound();
                _cache.Set(metadataKey, song, TimeSpan.FromMinutes(30));
            }

            if (string.IsNullOrWhiteSpace(song?.ArtworkUri))
            {
                return NotFound("No artwork available.");
            }

            var artworkStr = song.ArtworkUri;

            if (artworkStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                artworkStr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(artworkStr);
            }

            try
            {
                var (stream, contentType, contentLength) = await _storageService.GetFileStreamAsync(artworkStr);
                return File(stream, contentType);
            }
            catch (Exception ex)
            {
                return NotFound($"Artwork file error: {ex.Message}");
            }
        }

        [HttpGet("artwork/{fileKey}")]
        public async Task<IActionResult> GetArtworkByKey(string fileKey)
        {
            if (string.IsNullOrWhiteSpace(fileKey)) return NotFound();

            try
            {
                var (stream, contentType, contentLength) = await _storageService.GetFileStreamAsync(fileKey);
                return File(stream, contentType);
            }
            catch (Exception ex)
            {
                return NotFound($"Artwork file error: {ex.Message}");
            }
        }

        [HttpPost("migrate-artworks-to-r2")]
        public async Task<IActionResult> MigrateArtworksToR2()
        {
            var songsWithBase64 = await _context.Songs
                .Where(s => s.ArtworkUri != null && s.ArtworkUri.StartsWith("data:image/"))
                .ToListAsync();

            int migratedCount = 0;

            foreach (var song in songsWithBase64)
            {
                try
                {
                    var commaIndex = song.ArtworkUri!.IndexOf(",");
                    if (commaIndex > 0)
                    {
                        var header = song.ArtworkUri.Substring(0, commaIndex);
                        var base64Data = song.ArtworkUri.Substring(commaIndex + 1);
                        var bytes = Convert.FromBase64String(base64Data);

                        string contentType = "image/png";
                        if (header.Contains("image/jpeg") || header.Contains("image/jpg")) contentType = "image/jpeg";
                        else if (header.Contains("image/gif")) contentType = "image/gif";
                        else if (header.Contains("image/webp")) contentType = "image/webp";

                        string ext = contentType.Split('/')[1];
                        using var artStream = new MemoryStream(bytes);
                        var r2Key = await _storageService.UploadFileAsync(artStream, $"migrated_{song.Id}.{ext}", contentType);

                        song.ArtworkUri = r2Key;
                        migratedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error migrating artwork for song {song.Id}: {ex.Message}");
                }
            }

            if (migratedCount > 0)
            {
                await _context.SaveChangesAsync();
                _cache.Remove(AllSongsCacheKey);
            }

            return Ok(new { message = $"Successfully migrated {migratedCount} existing artwork images to Cloudflare R2.", totalEvaluated = songsWithBase64.Count });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var song = await _context.Songs.FindAsync(id);
            if (song == null)
            {
                return NotFound();
            }

            try
            {
                // Delete audio file from R2
                await _storageService.DeleteFileAsync(song.FileKey);

                // Delete artwork file from R2 if stored as an R2 key
                if (!string.IsNullOrWhiteSpace(song.ArtworkUri) &&
                    !song.ArtworkUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !song.ArtworkUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !song.ArtworkUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                    !song.ArtworkUri.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await _storageService.DeleteFileAsync(song.ArtworkUri);
                    }
                    catch
                    {
                        // Ignore if artwork file missing in storage
                    }
                }

                _context.Songs.Remove(song);
                await _context.SaveChangesAsync();

                // Evict cache entries
                _cache.Remove(AllSongsCacheKey);
                _cache.Remove(SongMetadataCacheKey(id));
                _cache.Remove(SongAudioBytesCacheKey(id));
                _cache.Remove(SongAudioTypeCacheKey(id));

                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting song: {ex.Message}");
            }
        }
    }
}
