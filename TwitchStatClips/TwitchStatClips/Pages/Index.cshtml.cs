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
            var clientId = _config["Twitch:ClientId"];
            var redirectUri = _config["Twitch:RedirectUri"];
            var scope = "user:read:email clips:edit";

            var loginUrl = $"https://id.twitch.tv/oauth2/authorize?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={Uri.EscapeDataString(scope)}";

            return Redirect(loginUrl);
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
