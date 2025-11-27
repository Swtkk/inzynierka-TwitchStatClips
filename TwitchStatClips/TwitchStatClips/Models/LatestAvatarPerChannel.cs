using Microsoft.EntityFrameworkCore;
using System;

namespace TwitchStatClips.Models
{
    [Keyless]
    public class LatestAvatarPerChannel
    {
        public string ChannelLogin { get; set; } = default!;
        public string AvatarUrl { get; set; } = default!;
        public DateTime LastSeenAtUtc { get; set; }
    }
}
