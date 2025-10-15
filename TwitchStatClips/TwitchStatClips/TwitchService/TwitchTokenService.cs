using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TwitchStatClips.TwitchService
{
    public class TwitchTokenService
    {
        private readonly IConfiguration _config;
        private TwitchAuthToken? _currentToken;
        private readonly object _lock = new();
        private readonly IMemoryCache _cache;
        private readonly IHttpClientFactory _httpFactory;  
        private readonly IConfiguration _cfg;

        public TwitchTokenService(IConfiguration config, IMemoryCache cache, IHttpClientFactory httpFactory,
        IConfiguration cfg)
        {
            _httpFactory = httpFactory;
            _cfg = cfg;
            _config = config;
            _cache = cache;
        }
        public async Task<(string? Name, string? AvatarUrl)> GetUserInfoAsync()
        {
            var token = GetToken();
            if (token == null) return (null, null);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", _config["Twitch:ClientId"]);

            var response = await client.GetAsync("https://api.twitch.tv/helix/users");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var user = doc.RootElement.GetProperty("data")[0];

            return (
                user.GetProperty("display_name").GetString(),
                user.GetProperty("profile_image_url").GetString()
            );
        }

        public TwitchAuthToken? GetToken()
        {
            // Zawsze próbuj najpierw z cache
            if (_cache.TryGetValue("twitch_token", out TwitchAuthToken token))
            {
                _currentToken = token;
                return token;
            }

            return null;
        }
        public async Task<string?> GetUserNameAsync()
        {
            var token = GetToken();
            if (token == null) return null;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", _config["Twitch:ClientId"]);

            var response = await client.GetAsync("https://api.twitch.tv/helix/users");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            return doc.RootElement.GetProperty("data")[0].GetProperty("display_name").GetString();
        }


        public bool IsTokenAvailable()
        {
            var token = GetToken();
            return token != null && !token.IsExpired;
        }

        public async Task RequestAppTokenAsync()
        {
            var clientId = _config["Twitch:ClientId"];
            var clientSecret = _config["Twitch:ClientSecret"];

            var body = new Dictionary<string, string>
    {
        { "client_id", clientId },
        { "client_secret", clientSecret },
        { "grant_type", "client_credentials" }
    };

            using var client = new HttpClient();
            var response = await client.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(body));
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var token = new TwitchAuthToken
            {
                AccessToken = doc.RootElement.GetProperty("access_token").GetString()!,
                RefreshToken = "", // brak przy client_credentials
                ExpiresAt = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32()),
                TokenType = doc.RootElement.GetProperty("token_type").GetString()!
            };

            lock (_lock)
            {
                _currentToken = token;
            }

            _cache.Set("twitch_token", token, token.ExpiresAt - DateTime.UtcNow);
        }

        public async Task<TwitchAuthToken> ExchangeCodeForTokenAsync(string code)
        {
            var clientId = _config["Twitch:ClientId"];
            var clientSecret = _config["Twitch:ClientSecret"];
            var redirectUri = _config["Twitch:RedirectUri"];

            var body = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", redirectUri }
            };

            using var client = new HttpClient();
            var response = await client.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(body));
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var token = new TwitchAuthToken
            {
                AccessToken = doc.RootElement.GetProperty("access_token").GetString()!,
                RefreshToken = doc.RootElement.GetProperty("refresh_token").GetString()!,
                ExpiresAt = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32()),
                TokenType = doc.RootElement.GetProperty("token_type").GetString()!
            };

            lock (_lock)
            {
                _currentToken = token;
            }

            _cache.Set("twitch_token", token, token.ExpiresAt - DateTime.UtcNow);
            return token;
        }

        public async Task RefreshTokenAsync()
        {
            var token = GetToken();
            if (token == null) return;

            _cache.Set("twitch_token", token, token.ExpiresAt - DateTime.UtcNow);

            if (_currentToken == null || string.IsNullOrEmpty(_currentToken.RefreshToken))
                return;

            var clientId = _config["Twitch:ClientId"];
            var clientSecret = _config["Twitch:ClientSecret"];

            var body = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", _currentToken.RefreshToken },
                { "client_id", clientId },
                { "client_secret", clientSecret }
            };

            using var client = new HttpClient();
            var response = await client.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(body));
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            lock (_lock)
            {
                _currentToken = new TwitchAuthToken
                {
                    AccessToken = doc.RootElement.GetProperty("access_token").GetString()!,
                    RefreshToken = doc.RootElement.GetProperty("refresh_token").GetString()!,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32()),
                    TokenType = doc.RootElement.GetProperty("token_type").GetString()!
                };
            }

            _cache.Set("twitch_token", _currentToken, _currentToken.ExpiresAt - DateTime.UtcNow);
        }

        public async Task<string?> GetUserIdAsync()
        {
            var token = GetToken();
            if (token == null) return null;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", _config["Twitch:ClientId"]);

            var response = await client.GetAsync("https://api.twitch.tv/helix/users");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            return doc.RootElement.GetProperty("data")[0].GetProperty("id").GetString();
        }

        public async Task<List<TwitchClip>> GetClipsAsync(string userId)
        {
            var token = GetToken();
            if (token == null) return new();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", _config["Twitch:ClientId"]);

            var response = await client.GetAsync($"https://api.twitch.tv/helix/clips?broadcaster_id={userId}&first=10");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var clips = new List<TwitchClip>();

            foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                clips.Add(new TwitchClip
                {
                    Id = item.GetProperty("id").GetString(),
                    Url = item.GetProperty("url").GetString(),
                    EmbedUrl = item.GetProperty("embed_url").GetString(),
                    Title = item.GetProperty("title").GetString(),
                    BroadcasterName = item.GetProperty("broadcaster_name").GetString(),
                    ThumbnailUrl = item.GetProperty("thumbnail_url").GetString(),
                    CreatedAt = item.GetProperty("created_at").GetDateTime()
                });
            }

            return clips;
        }

        public async Task<List<(string Id, string Name)>> GetTopGamesAsync()
        {
            var token = GetToken();
            if (token == null) return new();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", _config["Twitch:ClientId"]);

            var response = await client.GetAsync("https://api.twitch.tv/helix/games/top?first=50");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var result = new List<(string, string)>();
            foreach (var game in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                result.Add((
                    game.GetProperty("id").GetString()!,
                    game.GetProperty("name").GetString()!
                ));
            }

            return result;
        }

        public async Task<string?> GetGameIdByNameAsync(string gameName)
        {
            var token = GetToken();
            if (token == null) return null;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", _config["Twitch:ClientId"]);

            var response = await client.GetAsync($"https://api.twitch.tv/helix/games?name={Uri.EscapeDataString(gameName)}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0)
                return null;

            return data[0].GetProperty("id").GetString();
        }
        public void ClearToken()
        {
            _cache.Remove("twitch_token");
            lock (_lock)
            {
                _currentToken = null;
            }
        }

        public async Task<List<TwitchClip>> GetClipsByGameAsync(string gameId, string? period = null)
        {
            string cacheKey = $"clips_{gameId}_{period}";

            if (_cache.TryGetValue(cacheKey, out List<TwitchClip> cachedClips))
            {
                Console.WriteLine($"🔁 Loaded from CACHE for key: {cacheKey}");
                return cachedClips;
            }

            var token = GetToken();
            if (token == null) return new();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", _config["Twitch:ClientId"]);

            var baseUrl = $"https://api.twitch.tv/helix/clips?game_id={gameId}&first=20";

            if (!string.IsNullOrEmpty(period) && period != "all")
            {
                var now = DateTime.UtcNow;
                DateTime startedAt = period switch
                {
                    "day" => now.AddDays(-1),
                    "week" => now.AddDays(-7),
                    "month" => now.AddMonths(-1),
                    _ => now
                };
                baseUrl += $"&started_at={Uri.EscapeDataString(startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"))}";
            }

            var response = await client.GetAsync(baseUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var result = new List<TwitchClip>();

            foreach (var clip in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                result.Add(new TwitchClip
                {
                    Id = clip.GetProperty("id").GetString(),
                    Url = clip.GetProperty("url").GetString(),
                    EmbedUrl = clip.GetProperty("embed_url").GetString(),
                    Title = clip.GetProperty("title").GetString(),
                    BroadcasterName = clip.GetProperty("broadcaster_name").GetString(),
                    ThumbnailUrl = clip.GetProperty("thumbnail_url").GetString(),
                    CreatedAt = clip.GetProperty("created_at").GetDateTime()
                });
            }

            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
            Console.WriteLine($"🌐 Loaded from API for key: {cacheKey}");

            return result;
        }




        public async Task<string?> GetUserIdByNameAsync(string username)
        {
            var token = GetToken();
            if (token == null) return null;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", _config["Twitch:ClientId"]);

            var response = await client.GetAsync($"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(username)}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0)
                return null;

            return data[0].GetProperty("id").GetString();
        }

    



    public async Task LogUserInfoByIdAsync(string id)
        {
            var token = GetToken();
            if (token == null)
            {
                Console.WriteLine("❌ Token is null — nie można pobrać danych użytkownika.");
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", _config["Twitch:ClientId"]);

            var response = await client.GetAsync($"https://api.twitch.tv/helix/users?id={id}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ Błąd pobierania danych użytkownika: {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var user = doc.RootElement.GetProperty("data")[0];

            Console.WriteLine("🟣 Dane użytkownika:");
            foreach (var property in user.EnumerateObject())
            {
                Console.WriteLine($"  {property.Name}: {property.Value}");
            }
        }

        public async Task<(string AccessToken, string RefreshToken, int ExpiresIn)?> ExchangeAuthorizationCodeAsync(string code, string redirectUri)
        {
            var clientId = _cfg["Twitch:ClientId"];
            var clientSecret = _cfg["Twitch:ClientSecret"];

            var content = new FormUrlEncodedContent(new[]
            {
        new KeyValuePair<string,string>("client_id", clientId),
        new KeyValuePair<string,string>("client_secret", clientSecret),
        new KeyValuePair<string,string>("code", code),
        new KeyValuePair<string,string>("grant_type", "authorization_code"),
        new KeyValuePair<string,string>("redirect_uri", redirectUri),
    });

            using var http = _httpFactory.CreateClient();
            var resp = await http.PostAsync("https://id.twitch.tv/oauth2/token", content);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return (json.GetProperty("access_token").GetString()!,
                    json.GetProperty("refresh_token").GetString()!,
                    json.GetProperty("expires_in").GetInt32());
        }

        public async Task<(string Id, string Name, string AvatarUrl)>
    GetUserInfoAsync(string accessToken)
        {
            using var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            http.DefaultRequestHeaders.Add("Client-Id", _cfg["Twitch:ClientId"]);

            var json = await http.GetFromJsonAsync<JsonElement>("https://api.twitch.tv/helix/users");
            var data = json.GetProperty("data")[0];

            return (
                data.GetProperty("id").GetString()!,
                data.GetProperty("display_name").GetString() ?? "",
                data.GetProperty("profile_image_url").GetString() ?? ""
            );
        }

    }
}
