using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicPlatform.API.Data;
using MusicPlatform.API.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MusicPlatform.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PlaylistsController : ControllerBase
    {
        private readonly MusicDbContext _context;
        private static readonly Guid LikedSongsGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public PlaylistsController(MusicDbContext context)
        {
            _context = context;
        }

        public class PlaylistDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public List<string> SongIds { get; set; } = new();
        }

        public class CreatePlaylistRequest
        {
            public string Name { get; set; } = string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var playlists = await _context.Playlists
                .Include(p => p.Songs)
                .ToListAsync();

            var likedSongsPlaylist = playlists.FirstOrDefault(p => p.Id == LikedSongsGuid);
            if (likedSongsPlaylist == null)
            {
                likedSongsPlaylist = playlists.FirstOrDefault(p => p.Name.Equals("Liked Songs", StringComparison.OrdinalIgnoreCase));
                if (likedSongsPlaylist == null)
                {
                    likedSongsPlaylist = new Playlist
                    {
                        Id = LikedSongsGuid,
                        Name = "Liked Songs",
                        Songs = new List<Song>()
                    };
                    _context.Playlists.Add(likedSongsPlaylist);
                    try
                    {
                        await _context.SaveChangesAsync();
                        playlists.Add(likedSongsPlaylist);
                    }
                    catch (DbUpdateException)
                    {
                        playlists = await _context.Playlists.Include(p => p.Songs).ToListAsync();
                    }
                }
            }

            var dtos = playlists.Select(p => new PlaylistDto
            {
                Id = p.Id.ToString(),
                Name = p.Name,
                SongIds = p.Songs.Select(s => s.Id.ToString()).ToList()
            }).ToList();

            return Ok(dtos);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePlaylistRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Playlist name cannot be empty.");
            }

            var playlist = new Playlist
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Songs = new List<Song>()
            };

            _context.Playlists.Add(playlist);
            await _context.SaveChangesAsync();

            var dto = new PlaylistDto
            {
                Id = playlist.Id.ToString(),
                Name = playlist.Name,
                SongIds = new List<string>()
            };

            return Ok(dto);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (id == LikedSongsGuid)
            {
                return BadRequest("The Liked Songs playlist cannot be deleted.");
            }

            var playlist = await _context.Playlists.FindAsync(id);
            if (playlist == null)
            {
                return NotFound("Playlist not found.");
            }

            _context.Playlists.Remove(playlist);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{id}/songs/{songId}")]
        public async Task<IActionResult> AddSong(Guid id, Guid songId)
        {
            var playlist = await _context.Playlists
                .Include(p => p.Songs)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (playlist == null)
            {
                return NotFound("Playlist not found.");
            }

            var song = await _context.Songs.FindAsync(songId);
            if (song == null)
            {
                return NotFound("Song not found.");
            }

            if (!playlist.Songs.Any(s => s.Id == songId))
            {
                playlist.Songs.Add(song);
                await _context.SaveChangesAsync();
            }

            return Ok(new PlaylistDto
            {
                Id = playlist.Id.ToString(),
                Name = playlist.Name,
                SongIds = playlist.Songs.Select(s => s.Id.ToString()).ToList()
            });
        }

        [HttpDelete("{id}/songs/{songId}")]
        public async Task<IActionResult> RemoveSong(Guid id, Guid songId)
        {
            var playlist = await _context.Playlists
                .Include(p => p.Songs)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (playlist == null)
            {
                return NotFound("Playlist not found.");
            }

            var song = playlist.Songs.FirstOrDefault(s => s.Id == songId);
            if (song != null)
            {
                playlist.Songs.Remove(song);
                await _context.SaveChangesAsync();
            }

            return Ok(new PlaylistDto
            {
                Id = playlist.Id.ToString(),
                Name = playlist.Name,
                SongIds = playlist.Songs.Select(s => s.Id.ToString()).ToList()
            });
        }
    }
}
