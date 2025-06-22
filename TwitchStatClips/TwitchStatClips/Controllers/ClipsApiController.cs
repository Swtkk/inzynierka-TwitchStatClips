using Microsoft.AspNetCore.Mvc;
using TwitchStatClips.TwitchService;

namespace TwitchStatClips.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClipsController : ControllerBase
    {
        private readonly TwitchTokenService _tokenService;
        private readonly TwitchClipService _clipService;

        public ClipsController(TwitchTokenService tokenService, TwitchClipService clipService)
        {
            _tokenService = tokenService;
            _clipService = clipService;
        }

        [HttpGet]
        public async Task<IActionResult> GetClips(string gameName, string? period = "week", int page = 1, int pageSize = 100)
        {
            if (!_tokenService.IsTokenAvailable())
                return Unauthorized();

            var gameId = await _tokenService.GetGameIdByNameAsync(gameName);
            if (gameId == null)
                return NotFound("Game not found");

            var result = await _clipService.GetPagedClipsAsync(gameId, period, page, pageSize);
            return Ok(result);
        }
    }
}
