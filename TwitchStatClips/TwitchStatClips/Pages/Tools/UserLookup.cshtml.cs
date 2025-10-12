using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TwitchStatClips.TwitchService;

public class UserLookupModel : PageModel
{
    private readonly TwitchTokenService _tokenService;

    public UserLookupModel(TwitchTokenService tokenService)
    {
        _tokenService = tokenService;
    }

    [BindProperty]
    public string Nickname { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public async Task<IActionResult> OnPostAsync()
    {
        if (!string.IsNullOrWhiteSpace(Nickname))
        {
            UserId = await _tokenService.GetUserIdByNameAsync(Nickname) ?? "";
        }
        await _tokenService.LogUserInfoByIdAsync(UserId);

        return Page();
    }
}
