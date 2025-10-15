using System.ComponentModel.DataAnnotations;

namespace TwitchStatClips.Models.DTO
{
    public class FavoriteClipDto
    {
        [Required] public string ClipId { get; set; } = default!;
        public string? Title { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? BroadcasterName { get; set; }
        public string? EmbedUrl { get; set; }
    }
}
