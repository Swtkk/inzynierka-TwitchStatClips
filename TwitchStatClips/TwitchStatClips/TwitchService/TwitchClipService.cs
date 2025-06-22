using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace TwitchStatClips.TwitchService
{
    public class TwitchClipService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;

        public TwitchClipService(HttpClient httpClient, IMemoryCache cache, IConfiguration config)
        {
            _httpClient = httpClient;
            _cache = cache;
            _config = config;
        }

        public async Task<object> GetPagedClipsAsync(string gameId, string? period, int page, int pageSize)
        {
            string cacheKey = $"clips_{gameId}_{period}_batch_{(page - 1) / 10}";
            if (!_cache.TryGetValue(cacheKey, out List<TwitchClip> fullBatch))
            {
                string baseUrl = $"https://api.twitch.tv/helix/clips?game_id={gameId}&first=100";

                if (!string.IsNullOrEmpty(period) && period != "all")
                {
                    var now = DateTime.UtcNow;
                    var startedAt = period switch
                    {
                        "day" => now.AddDays(-1),
                        "week" => now.AddDays(-7),
                        "month" => now.AddMonths(-1),
                        _ => now
                    };
                    baseUrl += $"&started_at={Uri.EscapeDataString(startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"))}";
                }

                var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
                request.Headers.Add("Authorization", $"Bearer {_cache.Get<TwitchAuthToken>("twitch_token")?.AccessToken}");
                request.Headers.Add("Client-Id", _config["Twitch:ClientId"]);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                fullBatch = doc.RootElement.GetProperty("data").EnumerateArray()
                    .Select(item => new TwitchClip
                    {
                        Id = item.GetProperty("id").GetString(),
                        Url = item.GetProperty("url").GetString(),
                        EmbedUrl = item.GetProperty("embed_url").GetString(),
                        Title = item.GetProperty("title").GetString(),
                        BroadcasterName = item.GetProperty("broadcaster_name").GetString(),
                        ThumbnailUrl = item.GetProperty("thumbnail_url").GetString(),
                        CreatedAt = item.GetProperty("created_at").GetDateTime()
                    }).ToList();

                _cache.Set(cacheKey, fullBatch, TimeSpan.FromMinutes(5));
                Console.WriteLine($"🌐 Loaded batch from Twitch for key: {cacheKey}");
            }
            else
            {
                Console.WriteLine($"✅ Loaded batch from CACHE: {cacheKey}");
            }

            var paged = fullBatch
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            bool hasMore = (page * pageSize) < fullBatch.Count;

            return new
            {
                clips = paged,
                hasMore
            };
        }
    }
}
