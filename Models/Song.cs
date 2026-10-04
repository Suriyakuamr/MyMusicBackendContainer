using System;

namespace MusicPlatform.API.Models
{
    public class Song
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string FileKey { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public double Duration { get; set; } // in seconds
        public long FileSize { get; set; } // in bytes
        public string? ArtworkUri { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
