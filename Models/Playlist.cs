using System;
using System.Collections.Generic;

namespace MusicPlatform.API.Models
{
    public class Playlist
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ICollection<Song> Songs { get; set; } = new List<Song>();
    }
}
