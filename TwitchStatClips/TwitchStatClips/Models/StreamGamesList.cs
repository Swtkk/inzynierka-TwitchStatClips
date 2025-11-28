using Microsoft.EntityFrameworkCore;

namespace TwitchStatClips.Models
{
    [Keyless]
    public class StreamGamesList
    {
        public string ChannelLogin { get; set; } = default!;
        public string Games { get; set; } = default!;
    }
}
