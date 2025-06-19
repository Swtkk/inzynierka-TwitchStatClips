using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TwitchStatClips.TwitchService;

public class IndexClipsModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly TwitchTokenService _tokenService;

    public IndexClipsModel(IConfiguration config, TwitchTokenService tokenService)
    {
        _config = config;
        _tokenService = tokenService;
    }

    public List<TwitchClip> Clips { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string? gameName, string? period, string? language)
    {
        gameName ??= "Just Chatting";
        period ??= "week";

        var gameId = await _tokenService.GetGameIdByNameAsync(gameName);
        if (gameId != null)
        {
            Clips = await _tokenService.GetClipsByGameAsync(gameId, period);
        }

        return Page();
    }
}
