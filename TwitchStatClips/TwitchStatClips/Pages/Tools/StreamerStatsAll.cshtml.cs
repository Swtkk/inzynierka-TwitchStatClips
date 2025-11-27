using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TwitchStatClips.Data;
using TwitchStatClips.Models;
using TwitchStatClips.Models.ViewModels;

namespace TwitchStatClips.Pages.Tools
{
    [Authorize(AuthenticationSchemes = "AppCookie")]
    public class StreamerStatsAllModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly ILogger<StreamerStatsAllModel> _logger;

        public StatsPageViewModel Data { get; private set; } = new();

        public StreamerStatsAllModel(AppDbContext db, ILogger<StreamerStatsAllModel> logger)
        {
            _db = db;
            _logger = logger;
        }

        // /Tools/StreamerStatsAll?range=24h&pageNumber=1&pageSize=50&sortBy=avg&sortDir=desc
        public async Task OnGetAsync(
            string range = "24h",
            int pageNumber = 1,
            int pageSize = 50,
            string sortBy = "avg",
            string sortDir = "desc",
            string? language = null,
            string? game = null)
        {
            if (pageSize <= 0) pageSize = 50;
            if (pageSize > 100) pageSize = 100;
            if (pageNumber <= 0) pageNumber = 1;

            sortBy = sortBy?.ToLowerInvariant() ?? "avg";
            sortDir = sortDir?.ToLowerInvariant() == "asc" ? "asc" : "desc";

            string viewName = range switch
            {
                "7d" => "GetStats_7d",
                "30d" => "GetStats_30d",
                "all" => "GetStats_AllTime",
                _ => "GetStats_24h"
            };



            try
            {
                var statsQuery = _db.GetStats
                 .FromSqlRaw($"SELECT * FROM dbo.{viewName}");

                // opcje do dropdownów (bez paginacji, ale w obrêbie zakresu)
                var languageOptions = await statsQuery
                    .Select(s => s.CurrentLanguage)
                    .Where(l => l != null && l != "")
                    .Distinct()
                    .OrderBy(l => l)
                    .ToListAsync();

                var gameOptions = await statsQuery
                    .Select(s => s.CurrentGame)
                    .Where(g => g != null && g != "")
                    .Distinct()
                    .OrderBy(g => g)
                    .ToListAsync();

                // bazowe zapytanie do dalszego sortowania / paginacji
                var baseQuery = statsQuery;

                // FILTRY
                if (!string.IsNullOrWhiteSpace(language))
                {
                    baseQuery = baseQuery.Where(s => s.CurrentLanguage == language);
                }

                if (!string.IsNullOrWhiteSpace(game))
                {
                    baseQuery = baseQuery.Where(s => s.CurrentGame == game);
                }

                // SORTOWANIE
                IOrderedQueryable<GetStats> ordered = sortBy switch
                {
                    "max" => sortDir == "asc"
                        ? baseQuery.OrderBy(s => s.MaxViewers)
                        : baseQuery.OrderByDescending(s => s.MaxViewers),

                    "hours" => sortDir == "asc"
                        ? baseQuery.OrderBy(s => s.HoursWatched)
                        : baseQuery.OrderByDescending(s => s.HoursWatched),

                    "followers" => sortDir == "asc"
                        ? baseQuery.OrderBy(s => s.FollowersLatest)
                        : baseQuery.OrderByDescending(s => s.FollowersLatest),

                    "current" => sortDir == "asc"
                    ? baseQuery.OrderBy(s => s.CurrentViewers ?? 0)
                    : baseQuery.OrderByDescending(s => s.CurrentViewers ?? 0),

                    _ => sortDir == "asc" // DEFAULT: avg
                        ? baseQuery.OrderBy(s => s.AvgViewers)
                        : baseQuery.OrderByDescending(s => s.AvgViewers),
                };

                var totalItems = await ordered.CountAsync();

                var items = await ordered
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var avatarDict = await _db.LatestAvatarPerChannel
                .ToDictionaryAsync(a => a.ChannelLogin, a => a.AvatarUrl);

                // 2. Wstrzyknij AvatarUrl do ka¿dego elementu statystyk
                foreach (var s in items)
                {
                    if (avatarDict.TryGetValue(s.ChannelLogin, out var url))
                    {
                        s.AvatarUrl = url;
                    }
                }


                var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

                Data = new StatsPageViewModel
                {
                    Items = items,
                    Range = range,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalItems = totalItems,
                    TotalPages = totalPages,
                    SortBy = sortBy,
                    SortDir = sortDir,
                    Language = language,
                    Game = game,
                    LanguageOptions = languageOptions,
                    GameOptions = gameOptions
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "B³¹d przy pobieraniu statystyk.");
                Data = new StatsPageViewModel
                {
                    Range = range,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    SortBy = sortBy,
                    SortDir = sortDir
                };
            }
        }
    }
}
