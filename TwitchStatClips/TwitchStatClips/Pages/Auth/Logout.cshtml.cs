using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TwitchStatClips.TwitchService;

public class LogoutModel : PageModel
{
    private readonly TwitchTokenService _tokenService;

    public LogoutModel(TwitchTokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public IActionResult OnGet()
    {
        _tokenService.ClearToken(); // Usuwa token z cache i z pamiêci
        return RedirectToPage("/Index");
    }
}
