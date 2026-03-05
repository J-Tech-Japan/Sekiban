using System.Net.Http.Headers;

public sealed class ColdExportTimerService : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(3);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ColdExportTimerService> _logger;

    public ColdExportTimerService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ColdExportTimerService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = ResolveInterval();
        var apiBaseUrl = ResolveApiBaseUrl();

        _logger.LogInformation(
            "Cold export timer started. Target={Target}, Interval={Interval}",
            apiBaseUrl,
            interval);

        using var timer = new PeriodicTimer(interval);

        await TriggerExportAsync(apiBaseUrl, stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TriggerExportAsync(apiBaseUrl, stoppingToken);
        }
    }

    private async Task TriggerExportAsync(string apiBaseUrl, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(apiBaseUrl), "/api/cold/export-now"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Cold export succeeded: {StatusCode} {Body}", (int)response.StatusCode, body);
                return;
            }

            _logger.LogWarning("Cold export failed: {StatusCode} {Body}", (int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cold export timer execution failed");
        }
    }

    private TimeSpan ResolveInterval()
    {
        var raw = _configuration["ColdExport:Interval"];
        if (!string.IsNullOrWhiteSpace(raw) && TimeSpan.TryParse(raw, out var parsed) && parsed > TimeSpan.Zero)
        {
            return parsed;
        }

        return DefaultInterval;
    }

    private string ResolveApiBaseUrl()
    {
        var configured = _configuration["ApiBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return EnsureTrailingSlash(configured);
        }

        return "http://withoutresultapiservice/";
    }

    private static string EnsureTrailingSlash(string baseUrl)
        => baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/";
}
