namespace TwitchStatClips.TwitchService
{
    public class TwitchTokenRefreshService : BackgroundService
    {
        private readonly TwitchTokenService _tokenService;
        private readonly ILogger<TwitchTokenRefreshService> _logger;

        public TwitchTokenRefreshService(TwitchTokenService service, ILogger<TwitchTokenRefreshService> logger)
        {
            _tokenService = service;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_tokenService.GetToken()?.IsExpired == true)
                {
                    _logger.LogInformation("Token wygasł – odświeżam...");
                    await _tokenService.RefreshTokenAsync();
                }

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
        }
    }

}
