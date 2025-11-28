using Microsoft.EntityFrameworkCore;

namespace TwitchStatClips.Models
{
    [Keyless]
    public class GetFollowers
    {
        public string ChannelLogin { get; set; } = default!;

        public int FollowersMax { get; set; }
        public int FollowersMin { get; set; }
        public int FollowersGained { get; set; }

        public DateTime FirstBucket { get; set; }
        public DateTime LastBucket { get; set; }

        public int? FollowersTotalNow { get; set; }   // w SQL widzę czasem NULL
    }
}
