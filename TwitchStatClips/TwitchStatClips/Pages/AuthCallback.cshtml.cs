using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Threading.Tasks;
using TwitchStatClips.TwitchService;

public class AuthCallbackModel : PageModel
{
    private readonly TwitchTokenService _tokenService;

    public AuthCallbackModel(TwitchTokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public async Task<IActionResult> OnGetAsync([FromQuery] string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest("Brak kodu logowania.");

        await _tokenService.ExchangeCodeForTokenAsync(code);

        return RedirectToPage("/Index"); // wraca do g³ównej strony
    }
}
