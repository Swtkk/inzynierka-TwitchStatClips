using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TwitchStatClips.Data;
using TwitchStatClips.TwitchService;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<TwitchTokenService>();
builder.Services.AddHostedService<TwitchTokenRefreshService>();
builder.Services.AddControllers();
builder.Services.AddHttpClient<TwitchClipService>();
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
{
    //options.Conventions.AddPageRoute("/Auth/Login", "auth/twitch/login");
    //options.Conventions.AddPageRoute("/Auth/Logout", "auth/twitch/logout");
});
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "AppCookie";
    options.DefaultSignInScheme       = "AppCookie";
    options.DefaultChallengeScheme    = "AppCookie";
})
.AddCookie("AppCookie", o =>
{
    o.Cookie.Name = "twitchstat.auth";
    o.LoginPath = "/auth/twitch/login";
    o.AccessDeniedPath = "/";

    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    o.Cookie.IsEssential = true;                           // ignoruje ewentualny mechanizm zgody na cookie

    o.SlidingExpiration = true;
    o.ExpireTimeSpan = TimeSpan.FromDays(30);

    o.Events = new Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationEvents
    {
        OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }
            ctx.Response.Redirect(ctx.RedirectUri); return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; }
            ctx.Response.Redirect(ctx.RedirectUri); return Task.CompletedTask;
        }
    };
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();



app.MapRazorPages();
app.MapControllers();
app.MapGet("/debug/auth", (HttpContext ctx) =>
{
    var u = ctx.User;
    return Results.Json(new
    {
        isAuth = u?.Identity?.IsAuthenticated ?? false,
        authType = u?.Identity?.AuthenticationType,
        id = u?.FindFirstValue(ClaimTypes.NameIdentifier),
        name = u?.FindFirstValue(ClaimTypes.Name)
    });
});

app.MapGet("/__endpoints", (IEnumerable<EndpointDataSource> sources) =>
{
    var routes = sources
        .SelectMany(s => s.Endpoints)
        .OfType<RouteEndpoint>()
        .Select(e => e.RoutePattern.RawText)
        .OrderBy(x => x)
        .ToArray();
    return Results.Json(routes);
});

app.Run();


