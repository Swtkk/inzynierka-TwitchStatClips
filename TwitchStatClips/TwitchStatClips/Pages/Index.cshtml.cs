using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TwitchStatClips.TwitchService;

public class IndexModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly TwitchTokenService _tokenService;
    public string? SelectedGame { get; set; }

    public IndexModel(IConfiguration config, TwitchTokenService tokenService)
    {
        _config = config;
        _tokenService = tokenService;
    }

    public List<TwitchClip> Clips { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string? gameName, string? period, string? language)
    {
        if (!_tokenService.IsTokenAvailable())
        {
            // Jeœli nie ma tokena — pobierz token aplikacyjny (nie u¿ytkownika)
            await _tokenService.RequestAppTokenAsync();
        }

        gameName ??= "Just Chatting";
        period ??= "week";
        SelectedGame = gameName;

        var gameId = await _tokenService.GetGameIdByNameAsync(gameName);
        if (gameId != null)
        {
            Clips = await _tokenService.GetClipsByGameAsync(gameId, period);
        }

        return Page();
    }



}
