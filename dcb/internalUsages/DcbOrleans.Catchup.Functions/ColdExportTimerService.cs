using System.Net.Http.Headers;
using System.Text.Json;

public sealed class ColdExportTimerService : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultCycleBudget = TimeSpan.FromMinutes(3);

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
        var requestTimeout = ResolveRequestTimeout();
        var cycleBudget = ResolveCycleBudget();

        _logger.LogInformation(
            "Cold export timer started. Target={Target}, Interval={Interval}, RequestTimeout={RequestTimeout}, CycleBudget={CycleBudget}",
            apiBaseUrl,
            interval,
            requestTimeout,
            cycleBudget);

        using var timer = new PeriodicTimer(interval);

        await RunCycleAsync(apiBaseUrl, requestTimeout, cycleBudget, stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(apiBaseUrl, requestTimeout, cycleBudget, stoppingToken);
        }
    }

    private async Task RunCycleAsync(
        string apiBaseUrl,
        TimeSpan requestTimeout,
        TimeSpan cycleBudget,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }

        var timeout = requestTimeout < cycleBudget ? requestTimeout : cycleBudget;
        if (timeout <= TimeSpan.Zero)
        {
            _logger.LogWarning(
                "Cold export cycle skipped because timeout resolved to non-positive. RequestTimeout={RequestTimeout}, CycleBudget={CycleBudget}",
                requestTimeout,
                cycleBudget);
            return;
        }

        var result = await TriggerExportAsync(apiBaseUrl, timeout, ct);

        _logger.LogInformation(
            "Cold export cycle finished. Attempts=1, Success={Success}, ExportedEvents={ExportedEvents}, Timeout={Timeout}",
            result.IsSuccess,
            result.ExportedEventCount,
            timeout);
    }

    private async Task<ExportCallResult> TriggerExportAsync(string apiBaseUrl, TimeSpan requestTimeout, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = requestTimeout;

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(apiBaseUrl), "/api/cold/export-now"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Cold export succeeded: {StatusCode} {Body}", (int)response.StatusCode, body);
                var parsed = ParseExportedEventCount(body);
                return new ExportCallResult(true, parsed);
            }

            _logger.LogWarning("Cold export failed: {StatusCode} {Body}", (int)response.StatusCode, body);
            return new ExportCallResult(false, 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown.
            return new ExportCallResult(false, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cold export timer execution failed");
            return new ExportCallResult(false, 0);
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

    private TimeSpan ResolveRequestTimeout()
    {
        var raw = _configuration["ColdExport:RequestTimeout"];
        if (!string.IsNullOrWhiteSpace(raw) && TimeSpan.TryParse(raw, out var parsed) && parsed > TimeSpan.Zero)
        {
            return parsed;
        }

        return DefaultRequestTimeout;
    }

    private TimeSpan ResolveCycleBudget()
    {
        var raw = _configuration["ColdExport:CycleBudget"];
        if (!string.IsNullOrWhiteSpace(raw) && TimeSpan.TryParse(raw, out var parsed) && parsed > TimeSpan.Zero)
        {
            return parsed;
        }

        return DefaultCycleBudget;
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

    private static int ParseExportedEventCount(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("exportedEventCount", out var countElement)
                && countElement.TryGetInt32(out var count))
            {
                return count;
            }
        }
        catch
        {
            // Ignore parse errors and treat as non-exporting response.
        }

        return 0;
    }

    private readonly record struct ExportCallResult(bool IsSuccess, int ExportedEventCount);
}
