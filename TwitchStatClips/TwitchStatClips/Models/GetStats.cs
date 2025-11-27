// Models/GetStats.cs
using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TwitchStatClips.Models
{
    [Keyless]
    public class GetStats
    {
        public string ChannelLogin { get; set; } = default!;

        public decimal AvgViewers { get; set; }
        public int MaxViewers { get; set; }
        public int MinutesStreamed { get; set; }
        public decimal HoursWatched { get; set; }

        public int? CurrentViewers { get; set; }
        public int? FollowersLatest { get; set; }
        public DateTime? LastSeenAt { get; set; }

        public string? CurrentLanguage { get; set; }
        public string? CurrentGame { get; set; }
        [NotMapped]
        public string? AvatarUrl { get; set; }
    }
}
