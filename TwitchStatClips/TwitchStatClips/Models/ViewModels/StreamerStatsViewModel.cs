using TwitchStatClips.Models;

namespace TwitchStatClips.Models.ViewModels
{
    public class StreamerStatsViewModel
    {
        public string ChannelLogin { get; set; } = default!;
        public string AvatarUrl { get; set; } = default!;

        public GetStats? Stats24h { get; set; }
        public GetStats? Stats7d { get; set; }
        public GetStats? Stats30d { get; set; }
        public GetStats? StatsAll { get; set; }

        // Wybrany zakres (dla zakładek)
        public string Range { get; set; } = "24h";

        public string? Games24h { get; set; }
        public string? Games7d { get; set; }
        public string? Games30d { get; set; }
        public string? GamesAll { get; set; }

        public GetFollowers? Followers24h { get; set; }
        public GetFollowers? Followers7d { get; set; }
        public GetFollowers? Followers30d { get; set; }
        public GetFollowers? FollowersAll { get; set; }

        public bool IsLive { get; set; }
        public string? StreamTitle { get; set; }
        public string? OfflineImageUrl { get; set; }
    }
}
