using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TwitchStatClips.Pages.Auth.Twitch
{
    public class LoginModel : PageModel
    {
        private readonly IConfiguration _config;

        public LoginModel(IConfiguration config)
        {
            _config = config;
        }

        public IActionResult OnGet()
        {
            var clientId = _config["Twitch:ClientId"];
            var redirectUri = _config["Twitch:RedirectUri"];
            var scope = "user:read:email";

            var loginUrl = $"https://id.twitch.tv/oauth2/authorize" +
                           $"?client_id={clientId}" +
                           $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                           $"&response_type=code" +
                           $"&scope={Uri.EscapeDataString(scope)}";

            return Redirect(loginUrl);
        }
    }
}
