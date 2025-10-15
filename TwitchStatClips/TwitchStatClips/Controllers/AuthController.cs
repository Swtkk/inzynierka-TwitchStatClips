using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TwitchStatClips.TwitchService;

namespace TwitchStatClips.Controllers;

[Route("auth/twitch")]
public class AuthController : Controller
{
    private readonly IConfiguration _cfg;
    private readonly TwitchTokenService _twitch; // masz już ten serwis

    public AuthController(IConfiguration cfg, TwitchTokenService twitch)
    {
        _cfg = cfg;
        _twitch = twitch;
    }
    [HttpGet("whoami")]
    public IActionResult WhoAmI() =>
    Ok(new
    {
        User.Identity?.IsAuthenticated,
        Name = User.FindFirst(ClaimTypes.Name)?.Value,
        Id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
    });

    // 1) redirect do Twitcha
    [HttpGet("login")]
    public IActionResult Login()
    {
        var clientId = _cfg["Twitch:ClientId"];
        var redirect = Uri.EscapeDataString(_cfg["Twitch:RedirectUri"]); // np. https://localhost:5227/auth/twitch/callback
        var scope = Uri.EscapeDataString("user:read:email");          // wystarczy do pobrania profilu
        var state = Guid.NewGuid().ToString("N");

        // zapamiętaj state (CSRF) – prosty wariant: cookie
        Response.Cookies.Append("twitch_oauth_state", state, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax });

        var url = $"https://id.twitch.tv/oauth2/authorize" +
                  $"?response_type=code&client_id={clientId}&redirect_uri={redirect}&scope={scope}&state={state}";

        return Redirect(url);
    }

    // 2) callback z code -> wymiana na tokeny -> pobranie usera -> SignIn
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string code, string state)
    {
        if (!Request.Cookies.TryGetValue("twitch_oauth_state", out var saved) || saved != state)
            return BadRequest("Invalid state.");

        var redirectUri = _cfg["Twitch:RedirectUri"];
        var token = await _twitch.ExchangeAuthorizationCodeAsync(code, redirectUri);
        if (token is null) return Problem("Token exchange failed.");

        var (id, name, avatar) = await _twitch.GetUserInfoAsync(token.Value.AccessToken);

        var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, id),    // ← KLUCZOWE
        new Claim(ClaimTypes.Name, name ?? ""),
        new Claim("avatar", avatar ?? "")
    };

        await HttpContext.SignInAsync("AppCookie",
    new ClaimsPrincipal(new ClaimsIdentity(claims, "AppCookie")),
    new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        // unikamy „back-forward cache” i starych wersji HTML
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Redirect($"/?_={ts}");
    }



    // 3) wylogowanie
    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("AppCookie");

        Response.Cookies.Delete("twitchstat.auth", new CookieOptions
        {
            Path = "/"
        });

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        return Redirect("/");
    }

    [HttpGet("force-login")]
    public async Task<IActionResult> ForceLogin()
    {
        var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, "test-user-123"),
        new Claim(ClaimTypes.Name, "Test User"),
        new Claim("avatar", "")
    };

        await HttpContext.SignInAsync("AppCookie",
            new ClaimsPrincipal(new ClaimsIdentity(claims, "AppCookie")),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

        return Ok("signed-in");
    }

}
