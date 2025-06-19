namespace TwitchStatClips.TwitchService
{
    public class TwitchClip
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public string EmbedUrl { get; set; }
        public string Title { get; set; }
        public string BroadcasterName { get; set; }
        public string ThumbnailUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }

}
