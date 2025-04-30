using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class RandomPollingService : BackgroundService
{
    private readonly ILogger<RandomPollingService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private async Task WaitForServerAsync(CancellationToken stoppingToken)
    {
        var maxAttempts = 10;
        var delay = TimeSpan.FromSeconds(3);
        var healthUrl = "http://server/health";
        var client = _httpClientFactory.CreateClient();

        for (int attempt = 1; attempt <= maxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                var response = await client.GetAsync(healthUrl, stoppingToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Server is healthy and ready.");
                    return;
                }

                _logger.LogWarning("Health check returned {StatusCode}. Retrying...", response.StatusCode);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Attempt {Attempt}: Unable to reach server at {Url}. Exception: {Message}", attempt, healthUrl, ex.Message);
            }

            await Task.Delay(delay, stoppingToken);
        }

        _logger.LogWarning("Health check failed after {MaxAttempts} attempts. Proceeding anyway.", maxAttempts);
    }



    private static readonly string[] Endpoints =
    {
        "http://server/items",
        "http://server/items/deadlock-error",
        "http://server/items/connection-pool-error",
        "http://server/items/transaction-error",
        "http://server/items/constraint-error",
        "http://server/items/timeout-error"
    };

    private readonly Random _random = new();

    public RandomPollingService(ILogger<RandomPollingService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForServerAsync(stoppingToken);

        var client = _httpClientFactory.CreateClient();

        while (!stoppingToken.IsCancellationRequested)
        {
            var endpoint = Endpoints[_random.Next(Endpoints.Length)];
            try
            {
                _logger.LogInformation("Polling {Endpoint}", endpoint);
                var response = await client.GetAsync(endpoint, stoppingToken);
                _logger.LogInformation("Response: {StatusCode}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling {Endpoint}", endpoint);
            }

            await Task.Delay(TimeSpan.FromSeconds(_random.Next(30, 120)), stoppingToken);
        }
    }
}