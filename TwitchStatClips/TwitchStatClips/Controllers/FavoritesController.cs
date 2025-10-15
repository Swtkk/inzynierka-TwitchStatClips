using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TwitchStatClips.Data;
using TwitchStatClips.Models;
using TwitchStatClips.Models.DTO;

namespace TwitchStatClips.Controllers
{
    [Authorize(AuthenticationSchemes = "AppCookie")]
    [ApiController]
    [Route("api/[controller]")]
    // Jeśli nie masz skonfigurowanego Cookie/Auth – na czas testu możesz to zdjąć:
    public class FavoritesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<FavoritesController> _log;

        public FavoritesController(AppDbContext db, ILogger<FavoritesController> log)
        {
            _db = db;
            _log = log;
        }

        private string? ResolveUserId()
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(uid))
                _log.LogWarning("ResolveUserId: brak claimu z ID (NameIdentifier/sub).");
            return uid;
        }

        [HttpGet("ids")]
        public async Task<IActionResult> GetIds()
        {
            try
            {
                var uid = ResolveUserId();
                if (string.IsNullOrEmpty(uid)) return Unauthorized();

                var ids = await _db.Favorites
                    .Where(f => f.UserId == uid)
                    .Select(f => f.ClipId)
                    .ToListAsync();

                return Ok(ids);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GET /api/favorites/ids: {Message}", ex.Message);
                return Problem(statusCode: 500, title: "Błąd serwera przy pobieraniu ulubionych (ids).");
            }
        }

        [HttpPost("toggle")]
        public async Task<IActionResult> Toggle([FromBody] FavoriteClipDto dto)
        {
            try
            {
                var uid = ResolveUserId();
                if (string.IsNullOrEmpty(uid)) return Unauthorized();
                if (dto == null || string.IsNullOrWhiteSpace(dto.ClipId))
                    return BadRequest(new { message = "Brak clipId." });

                var existing = await _db.Favorites
                    .FirstOrDefaultAsync(f => f.UserId == uid && f.ClipId == dto.ClipId);

                if (existing != null)
                {
                    _db.Favorites.Remove(existing);
                    await _db.SaveChangesAsync();
                    return Ok(new { isFavorite = false });
                }

                _db.Favorites.Add(new FavoriteClip
                {
                    UserId = uid,
                    ClipId = dto.ClipId,
                    Title = dto.Title ?? "",
                    ThumbnailUrl = dto.ThumbnailUrl ?? "",
                    BroadcasterName = dto.BroadcasterName ?? "",
                    EmbedUrl = dto.EmbedUrl ?? "",
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                return Ok(new { isFavorite = true });
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                // Drugi równoległy request – traktujemy jak „już dodane”
                return Ok(new { isFavorite = true });
            }
            catch (DbUpdateException dbex)
            {
                _log.LogError(dbex, "POST /api/favorites/toggle – DbUpdateException: {Message}", dbex.Message);
                return Problem(statusCode: 500, title: "Błąd bazy danych przy zapisie ulubionego.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "POST /api/favorites/toggle: {Message}", ex.Message);
                return Problem(statusCode: 500, title: "Błąd serwera przy zapisie ulubionego.");
            }
        }

        static bool IsDuplicateKey(DbUpdateException ex)
        {
            // SQL Server: 2601 (duplicate key), 2627 (unique constraint)
            return ex.InnerException is Microsoft.Data.SqlClient.SqlException sql &&
                   (sql.Number == 2601 || sql.Number == 2627);
        }


        [HttpGet]
        public async Task<IActionResult> GetPage([FromQuery] int page = 1, [FromQuery] int pageSize = 24)
        {
            try
            {
                var uid = ResolveUserId();
                if (string.IsNullOrEmpty(uid)) return Unauthorized();

                var q = _db.Favorites.Where(f => f.UserId == uid)
                                     .OrderByDescending(f => f.CreatedAt);

                var total = await q.CountAsync();
                var items = await q.Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(f => new
                    {
                        f.ClipId,
                        f.Title,
                        f.ThumbnailUrl,
                        f.BroadcasterName,
                        f.EmbedUrl,
                        f.CreatedAt
                    })
                    .ToListAsync();

                return Ok(new { total, items });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GET /api/favorites: {Message}", ex.Message);
                return Problem(statusCode: 500, title: "Błąd serwera przy pobieraniu ulubionych.");
            }
        }
    }
}
