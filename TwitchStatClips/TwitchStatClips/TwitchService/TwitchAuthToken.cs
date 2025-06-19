namespace TwitchStatClips.TwitchService
{
    public class TwitchAuthToken
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string TokenType { get; set; }
        public int ExpiresIn { get; set; }
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }

}
