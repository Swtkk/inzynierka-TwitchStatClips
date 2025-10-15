using System.ComponentModel.DataAnnotations;

namespace TwitchStatClips.Models
{
    public class FavoriteClip
    {
        public int Id { get; set; }

        [Required] public string UserId { get; set; } = default!;
        [Required] public string ClipId { get; set; } = default!;

        [Required] public string Title { get; set; } = default!;
        [Required] public string ThumbnailUrl { get; set; } = default!;
        [Required] public string BroadcasterName { get; set; } = default!;
        [Required] public string EmbedUrl { get; set; } = default!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
