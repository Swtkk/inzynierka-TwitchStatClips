//using Microsoft.AspNetCore.Authentication;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.RazorPages;
//using System.Security.Claims;
//using TwitchStatClips.TwitchService;

//namespace TwitchStatClips.Pages.Auth.Twitch
//{
//    public class AuthCallbackModel : PageModel
//    {
//        private readonly TwitchTokenService _tokenService;

//        public AuthCallbackModel(TwitchTokenService tokenService)
//        {
//            _tokenService = tokenService;
//        }

//        public async Task<IActionResult> OnGetAsync([FromQuery] string code)
//        {
//            if (string.IsNullOrWhiteSpace(code))
//                return BadRequest("Brak kodu logowania.");

//            var token = await _tokenService.ExchangeCodeForTokenAsync(code);
//            if (token == null) return BadRequest("Token exchange failed.");

//            // pobierz dane usera (masz obie wersje; tu używam tej z accessToken)
//            var (id, name, avatarUrl) = await _tokenService.GetUserInfoAsync(token.AccessToken);

//            var claims = new List<Claim>
//            {
//                new Claim(ClaimTypes.NameIdentifier, id ?? "unknown"),
//                new Claim(ClaimTypes.Name, name ?? "unknown"),
//                new Claim("avatar", avatarUrl ?? "")
//            };

//            await HttpContext.SignInAsync("AppCookie",
//                new ClaimsPrincipal(new ClaimsIdentity(claims, "AppCookie")),
//                new AuthenticationProperties
//                {
//                    IsPersistent = true,
//                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
//                });

//            return RedirectToPage("/Index");
//        }
//    }
//}
