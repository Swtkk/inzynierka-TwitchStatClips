using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.Web;
using TwitchStatClips.TwitchService;

namespace TwitchStatClips.Pages.Tools
{
    public class StreamerSearchModel : PageModel
    {
        private readonly TwitchTokenService _tokenService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<StreamerSearchModel> _logger;

        public StreamerSearchModel(
            TwitchTokenService tokenService,
            IHttpClientFactory httpClientFactory,
            ILogger<StreamerSearchModel> logger)
        {
            _tokenService = tokenService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public void OnGet()
        {
        }

        // AJAX: /Tools/StreamerSearch?handler=Search&term=xxx&limit=10
        public async Task<IActionResult> OnGetSearchAsync(string term, int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 3)
            {
                return new JsonResult(Array.Empty<StreamerResult>());
            }

            if (limit <= 0 || limit > 50) limit = 10;

            try
            {
                var results = await SearchStreamersAsync(term, limit);
                return new JsonResult(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "B³¹d podczas wyszukiwania streamerów dla frazy {Term}", term);
                return new JsonResult(Array.Empty<StreamerResult>());
            }
        }

        private async Task<List<StreamerResult>> SearchStreamersAsync(string term, int limit)
        {
            var token = await _tokenService.EnsureTokenAsync();
            if (token == null) return new List<StreamerResult>();

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.AccessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", HttpUtility.UrlDecode(
                HttpContext.RequestServices
                    .GetRequiredService<IConfiguration>()["Twitch:ClientId"] ?? string.Empty));

            var url =
                $"https://api.twitch.tv/helix/search/channels?query={Uri.EscapeDataString(term)}&first={limit}&live_only=false";

            var resp = await client.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            var list = new List<StreamerResult>();

            foreach (var item in data.EnumerateArray())
            {
                var displayName = item.GetProperty("display_name").GetString() ?? "";
                var login = item.GetProperty("broadcaster_login").GetString() ?? "";
                var thumb = item.TryGetProperty("thumbnail_url", out var thumbProp)
                    ? thumbProp.GetString() ?? ""
                    : "";
                var viewerCount = item.TryGetProperty("viewer_count", out var vcProp)
                    ? vcProp.GetInt32()
                    : 0;

                list.Add(new StreamerResult
                {
                    DisplayName = displayName,
                    Login = login,
                    AvatarUrl = thumb,
                    ViewerCount = viewerCount

                });
            }

            // sort po widzach malej¹co (top œredniej / aktualnych)
            return list
                .OrderByDescending(x => x.ViewerCount)
                .ToList();
        }

        public class StreamerResult
        {
            public string DisplayName { get; set; } = string.Empty;
            public string Login { get; set; } = string.Empty;
            public string AvatarUrl { get; set; } = string.Empty;
            public int ViewerCount { get; set; }
        }
    }
}
