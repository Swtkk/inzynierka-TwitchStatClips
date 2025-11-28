using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly IMemoryCache _cache;
        public StatsPageViewModel Data { get; private set; } = new();

        public StreamerStatsAllModel(AppDbContext db, ILogger<StreamerStatsAllModel> logger, IMemoryCache cache)
        {
            _db = db;
            _logger = logger;
            _cache = cache;
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
                // ================= CACHE =================
                string cacheKey = $"stats_{viewName}";

                List<GetStats> allStats;

                if (!_cache.TryGetValue(cacheKey, out allStats!))
                {
                    // Pierwsze wywo³anie – pobieramy z bazy
                    allStats = await _db.GetStats
                        .FromSqlRaw($"SELECT * FROM dbo.{viewName}")
                        .AsNoTracking()
                        .ToListAsync();

                    // Trzymamy np. 2 minuty
                    _cache.Set(cacheKey, allStats,
                        new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
                        });
                }

                // ================= OPCJE DO DROPDOWNÓW =================
                var languageOptions = allStats
                    .Select(s => s.CurrentLanguage)
                    .Where(l => !string.IsNullOrEmpty(l))
                    .Distinct()
                    .OrderBy(l => l)
                    .ToList();

                var gameOptions = allStats
                    .Select(s => s.CurrentGame)
                    .Where(g => !string.IsNullOrEmpty(g))
                    .Distinct()
                    .OrderBy(g => g)
                    .ToList();

                // ================= FILTRY (w pamiêci) =================
                IEnumerable<GetStats> query = allStats;

                if (!string.IsNullOrWhiteSpace(language))
                    query = query.Where(s => s.CurrentLanguage == language);

                if (!string.IsNullOrWhiteSpace(game))
                    query = query.Where(s => s.CurrentGame == game);

                // ================= SORTOWANIE (w pamiêci) =================
                IOrderedEnumerable<GetStats> ordered = sortBy switch
                {
                    "max" => sortDir == "asc"
                        ? query.OrderBy(s => s.MaxViewers)
                        : query.OrderByDescending(s => s.MaxViewers),

                    "hours" => sortDir == "asc"
                        ? query.OrderBy(s => s.HoursWatched)
                        : query.OrderByDescending(s => s.HoursWatched),

                    "followers" => sortDir == "asc"
                        ? query.OrderBy(s => s.FollowersLatest ?? 0)
                        : query.OrderByDescending(s => s.FollowersLatest ?? 0),

                    "current" => sortDir == "asc"
                        ? query.OrderBy(s => s.CurrentViewers ?? 0)
                        : query.OrderByDescending(s => s.CurrentViewers ?? 0),

                    // DEFAULT: avg
                    _ => sortDir == "asc"
                        ? query.OrderBy(s => s.AvgViewers)
                        : query.OrderByDescending(s => s.AvgViewers),
                };

                // ================= PAGINACJA (w pamiêci) =================
                var totalItems = ordered.Count();

                var items = ordered
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // ================= AVATARY =================
                var avatarDict = await _db.LatestAvatarPerChannel
                    .ToDictionaryAsync(a => a.ChannelLogin, a => a.AvatarUrl);

                foreach (var s in items)
                {
                    if (avatarDict.TryGetValue(s.ChannelLogin, out var url))
                        s.AvatarUrl = url;
                }

                var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

                Data = new StatsPageViewModel
                {
                    Items          = items,
                    Range          = range,
                    PageNumber     = pageNumber,
                    PageSize       = pageSize,
                    TotalItems     = totalItems,
                    TotalPages     = totalPages,
                    SortBy         = sortBy,
                    SortDir        = sortDir,
                    Language       = language,
                    Game           = game,
                    LanguageOptions = languageOptions,
                    GameOptions     = gameOptions
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "B³¹d przy pobieraniu statystyk.");
                Data = new StatsPageViewModel
                {
                    Range      = range,
                    PageNumber = pageNumber,
                    PageSize   = pageSize,
                    SortBy     = sortBy,
                    SortDir    = sortDir
                };
            }
        }

    }
}
