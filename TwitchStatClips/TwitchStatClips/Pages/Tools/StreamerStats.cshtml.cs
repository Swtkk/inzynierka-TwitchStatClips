using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TwitchStatClips.Data;
using TwitchStatClips.Models;
using TwitchStatClips.Models.ViewModels;
using TwitchStatClips.TwitchService;

namespace TwitchStatClips.Pages.Tools
{
    public class StreamerStatsModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly ILogger<StreamerStatsModel> _logger;

        public StreamerStatsViewModel Data { get; set; } = new();

        private readonly TwitchTokenService _twitch;

        public StreamerStatsModel(AppDbContext db,
                                  ILogger<StreamerStatsModel> logger,
                                  TwitchTokenService twitch)
        {
            _db = db;
            _logger = logger;
            _twitch = twitch;
        }

        public async Task<IActionResult> OnGetAsync(string channel, string range = "24h")
        {
            if (string.IsNullOrWhiteSpace(channel))
                return NotFound();

            range = (range ?? "24h").ToLowerInvariant();

            try
            {
                // 1. Dane u¿ytkownika z Twitcha (avatar, offline image)
                var user = await _twitch.GetUserAsync(channel);   // <-- upewnij siê, ¿e u¿ywasz tej metody z TwitchTokenService
                var avatarUrl = user?.Profile_Image_Url ?? "/img/avatar-placeholder.png";
                var offlineImage = user?.Offline_Image_Url;

                // 2. Informacja o streamie (czy live + tytu³)
                var stream = await _twitch.GetStreamByLoginAsync(channel); // Twoja metoda do /helix/streams
                bool isLive = stream != null;
                string? streamTitle = stream?.Title;

                // 3. Statystyki z widoków GetStats_*
                var stats24 = await GetStatsForRange(channel, "GetStats_24h");
                var stats7 = await GetStatsForRange(channel, "GetStats_7d");
                var stats30 = await GetStatsForRange(channel, "GetStats_30d");
                var statsAll = await GetStatsForRange(channel, "GetStats_AllTime");

                // 4. Followers z widoków GetFollowers_*
                var foll24 = await GetFollowersForRange(channel, "GetFollowers_24h");
                var foll7 = await GetFollowersForRange(channel, "GetFollowers_7d");
                var foll30 = await GetFollowersForRange(channel, "GetFollowers_30d");
                var follAll = await GetFollowersForRange(channel, "GetFollowers_AllTime");

                // 5. Gry z widoków GetStreamGamesList_*
                var games24 = await GetGamesForRange(channel, "GetStreamGamesList_24h", "Games24h");
                var games7 = await GetGamesForRange(channel, "GetStreamGamesList_7d", "Games7d");
                var games30 = await GetGamesForRange(channel, "GetStreamGamesList_30d", "Games30d");
                var gamesAll = await GetGamesForRange(channel, "GetStreamGamesList_AllTime", "GamesAllTime");

                // 6. Jedno, finalne przypisanie ViewModelu
                Data = new StreamerStatsViewModel
                {
                    ChannelLogin    = channel,
                    Range           = range,

                    // Twitch – avatar + stream
                    AvatarUrl       = avatarUrl,
                    OfflineImageUrl = offlineImage,
                    IsLive          = isLive,
                    StreamTitle     = streamTitle,

                    // Statystyki
                    Stats24h        = stats24,
                    Stats7d         = stats7,
                    Stats30d        = stats30,
                    StatsAll        = statsAll,

                    // Followers
                    Followers24h    = foll24,
                    Followers7d     = foll7,
                    Followers30d    = foll30,
                    FollowersAll    = follAll,

                    // Gry
                    Games24h        = games24,
                    Games7d         = games7,
                    Games30d        = games30,
                    GamesAll        = gamesAll
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "B³¹d przy pobieraniu statystyk streamera {Channel}", channel);
                return StatusCode(500);
            }

            return Page();
        }


        private async Task<GetStats?> GetStatsForRange(string channel, string viewName)
        {
            var query = _db.GetStats.FromSqlRaw($"SELECT * FROM dbo.{viewName}");
            return await query.FirstOrDefaultAsync(s => s.ChannelLogin == channel);
        }

        private async Task<string?> GetGamesForRange(string channel, string viewName, string columnName)
        {
            var sql = $"SELECT ChannelLogin, {columnName} AS Games FROM dbo.{viewName}";
            var query = _db.StreamGamesList.FromSqlRaw(sql);

            return await query
                .Where(g => g.ChannelLogin == channel)
                .Select(g => g.Games)
                .FirstOrDefaultAsync();
        }

        // ----------- METODY, KTÓRYCH BRAKOWA£O -----------

        // 1. Który zestaw statystyk jest aktywny (24h / 7d / 30d / all)
        public GetStats? GetActiveStats()
        {
            return Data.Range switch
            {
                "7d" => Data.Stats7d,
                "30d" => Data.Stats30d,
                "all" => Data.StatsAll,
                _ => Data.Stats24h
            };
        }

        // 2. Która lista gier jest aktywna
        public string? GetActiveGames()
        {
            return Data.Range switch
            {
                "7d" => Data.Games7d,
                "30d" => Data.Games30d,
                "all" => Data.GamesAll,
                _ => Data.Games24h
            };
        }

        // 3. £adny tekst nazwy zak³adki
        public string DisplayRange(string r) => r switch
        {
            "24h" => "Ostatnie 24h",
            "7d" => "7 dni",
            "30d" => "30 dni",
            "all" => "Ogólne",
            _ => r
        };

        // 4. Parsowanie stringa "Just Chatting (336m), Counter-Strike (241m)" -> lista gier
        public IEnumerable<(string Name, int Minutes)> ParseGameList(string? games)
        {
            if (string.IsNullOrWhiteSpace(games))
                return Enumerable.Empty<(string, int)>();

            var result = new List<(string, int)>();

            foreach (var part in games.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();

                var idx = p.LastIndexOf('(');
                if (idx <= 0) continue;

                var name = p[..idx].Trim();
                var minutesPart = p[(idx + 1)..].Trim(); // np. "336m)"

                minutesPart = minutesPart
                    .Replace("m)", "")
                    .Replace("m", "")
                    .Trim();

                if (!int.TryParse(minutesPart, out var minutes))
                    minutes = 0;

                result.Add((name, minutes));
            }

            return result;
        }

        // 5. URL do grafiki gry z Twitcha
        public string GetGameImage(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "/img/avatar-placeholder.png"; // mo¿esz zrobiæ osobny placeholder dla gier

            var slug = name.Replace(" ", "%20");
            return $"https://static-cdn.jtvnw.net/ttv-boxart/{slug}-144x192.jpg";
        }

        private async Task<GetFollowers?> GetFollowersForRange(string channel, string viewName)
        {
            var query = _db.GetFollowers.FromSqlRaw($"SELECT * FROM dbo.{viewName}");
            return await query.FirstOrDefaultAsync(f => f.ChannelLogin == channel);
        }
        public int? CalculateFollowersDiff()
        {
            var r = Data.Range;

            GetFollowers? current = r switch
            {
                "7d" => Data.Followers7d,
                "30d" => Data.Followers30d,
                "all" => Data.FollowersAll,
                _ => Data.Followers24h
            };

            GetFollowers? prev = r switch
            {
                "7d" => Data.Followers24h,
                "30d" => Data.Followers7d,
                "all" => Data.Followers30d,
                _ => null
            };

            if (current == null || prev == null)
                return null;

            if (current.FollowersTotalNow == null || prev.FollowersTotalNow == null)
                return null;

            return current.FollowersTotalNow.Value - prev.FollowersTotalNow.Value;
        }

    }
}
