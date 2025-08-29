using System.Net.Http.Json;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Shared state
var state = new BenchState();

app.MapGet("/", () => Results.Text($@"<!doctype html>
<html lang='ja'>
<head>
  <meta charset='utf-8'/>
  <meta name='viewport' content='width=device-width, initial-scale=1'/>
  <title>Bench Runner</title>
  <style>
    body {{ font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; margin: 2rem; }}
    .row {{ display: flex; gap: 1rem; align-items: center; margin-bottom: .75rem; }}
    input {{ width: 10rem; padding: .4rem .6rem; }}
    button {{ padding: .5rem 1rem; cursor: pointer; }}
    code {{ background: #f6f8fa; padding: .15rem .35rem; border-radius: .25rem; }}
    #log {{ white-space: pre-wrap; background:#0b1020; color:#e6edf3; padding:1rem; border-radius:.5rem; max-height:40vh; overflow:auto; }}
  </style>
  <script>
    async function fetchStatus() {{
      const r = await fetch('/status');
      const s = await r.json();
      document.getElementById('isRunning').textContent = s.isRunning ? 'Running' : 'Idle';
      document.getElementById('created').textContent = s.created;
      document.getElementById('updated').textContent = s.updated;
      document.getElementById('errors').textContent = s.errors;
      document.getElementById('elapsed').textContent = s.elapsed;
      if (s.created && s.elapsed) {{
        // rough throughput (create only)
      }}
    }}

    async function startRun() {{
      const total = document.getElementById('total').value || 10000;
      const conc = document.getElementById('conc').value || 32;
      const btn = document.getElementById('startBtn');
      btn.disabled = true;
      try {{
        const r = await fetch(`/run?total=${{total}}&concurrency=${{conc}}`, {{ method: 'POST' }});
        const t = await r.text();
        log(`POST /run => ${{r.status}}\n${{t}}`);
      }} catch (e) {{
        log('Failed to start: ' + e);
      }} finally {{
        btn.disabled = false;
      }}
    }}

    function log(msg) {{
      const el = document.getElementById('log');
      const now = new Date().toLocaleTimeString();
      el.textContent += `[${{now}}] ${{msg}}\n`;
      el.scrollTop = el.scrollHeight;
    }}

    setInterval(fetchStatus, 1000);
    window.addEventListener('load', fetchStatus);
  </script>
</head>
<body>
  <h1>Bench Runner</h1>
  <p>API: <code>{System.Net.WebUtility.HtmlEncode(Environment.GetEnvironmentVariable("ApiBaseUrl") ?? "(not set)")}</code></p>
  <div class='row'>
    <label>Total <input id='total' type='number' min='1' value='10000' /></label>
    <label>Concurrency <input id='conc' type='number' min='1' value='32' /></label>
    <button id='startBtn' onclick='startRun()'>開始</button>
    <a href='/status' target='_blank'>/status を開く</a>
  </div>
  <div class='row'>
    <div>状態: <strong id='isRunning'>-</strong></div>
    <div>作成: <strong id='created'>0</strong></div>
    <div>更新: <strong id='updated'>0</strong></div>
    <div>エラー: <strong id='errors'>0</strong></div>
    <div>経過: <strong id='elapsed'>00:00:00</strong></div>
  </div>
  <h3>ログ</h3>
  <div id='log'></div>
  <p style='margin-top:1rem'>
    エンドポイント: <code>POST /run?total=&lt;int&gt;&amp;concurrency=&lt;int&gt;</code>, <code>GET /status</code>
  </p>
</body>
</html>
", "text/html"));

app.MapGet("/status", () => Results.Json(new
{
    state.IsRunning,
    state.Created,
    state.Updated,
    state.Errors,
    Elapsed = state.Elapsed.ToString(),
    state.Total,
    state.Concurrency,
    state.StopOnError,
    state.Canceled,
    state.LastError
}));

app.MapPost("/run", async (int? total, int? concurrency, bool? stopOnError) =>
{
    if (state.IsRunning) return Results.BadRequest(new { message = "Already running" });

    var apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl")?.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(apiBase))
        return Results.BadRequest(new { message = "ApiBaseUrl env is not set" });

    state.Reset(total ?? GetEnvInt("BENCH_TOTAL", 10000), concurrency ?? GetEnvInt("BENCH_CONCURRENCY", 32));
    state.StopOnError = stopOnError ?? false;
    _ = Task.Run(() => RunAsync(apiBase!, state));
    return Results.Accepted($"/status", new { message = "Started", state.Total, state.Concurrency, state.StopOnError });
});

app.Run();
return;

static int GetEnvInt(string name, int def)
    => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : def;

static async Task RunAsync(string apiBase, BenchState state)
{
    try
    {
        state.IsRunning = true;
        var http = new HttpClient { BaseAddress = new Uri(apiBase) };
        var health = await http.GetAsync("/api/health");
        health.EnsureSuccessStatusCode();

        state.Start();
        var throttler = new SemaphoreSlim(Math.Max(1, state.Concurrency));
        var tasks = new List<Task>();
        var rnd = new Random();

        var token = state.Token;

        for (int i = 0; i < state.Total && !token.IsCancellationRequested; i++)
        {
            await throttler.WaitAsync();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;
                    var id = Guid.NewGuid();
                    var location = $"Loc-{Guid.NewGuid():N}";
                    var payload = new
                    {
                        forecastId = id,
                        location,
                        date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(rnd.Next(0, 30))),
                        temperatureC = rnd.Next(-10, 35),
                        summary = "bench"
                    };
                    var res = await http.PostAsJsonAsync("/api/inputweatherforecast", payload, token);
                    if (res.IsSuccessStatusCode)
                        Interlocked.Increment(ref state.Created);
                    else
                    {
                        Interlocked.Increment(ref state.Errors);
                        if (state.StopOnError)
                        {
                            state.LastError = $"Create failed: {(int)res.StatusCode} {res.ReasonPhrase}";
                            state.Cancel();
                            return;
                        }
                    }

                    var newName = location + "-U";
                    var upd = await http.PostAsJsonAsync("/api/updateweatherforecastlocation", new { forecastId = id, newLocationName = newName }, token);
                    if (upd.IsSuccessStatusCode)
                        Interlocked.Increment(ref state.Updated);
                    else
                    {
                        Interlocked.Increment(ref state.Errors);
                        if (state.StopOnError)
                        {
                            state.LastError = $"Update failed: {(int)upd.StatusCode} {upd.ReasonPhrase}";
                            state.Cancel();
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref state.Errors);
                    if (state.StopOnError)
                    {
                        state.LastError = ex.Message;
                        state.Cancel();
                    }
                }
                finally
                {
                    throttler.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        state.Stop();

        // verify query
        var listRes = await http.GetAsync($"/api/weatherforecast?pageNumber=1&pageSize=1000");
        if (listRes.IsSuccessStatusCode)
        {
            var json = await listRes.Content.ReadAsStringAsync();
            try
            {
                var doc = JsonDocument.Parse(json);
                state.QueryPageCount = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
            }
            catch { /* ignore */ }
        }
    }
    finally
    {
        state.IsRunning = false;
    }
}

sealed class BenchState
{
    public volatile bool IsRunning;
    public int Total;
    public int Concurrency;
    public int Created;
    public int Updated;
    public int Errors;
    public int QueryPageCount;
    public bool StopOnError;
    public bool Canceled => _cts?.IsCancellationRequested == true;
    public string? LastError;
    private System.Diagnostics.Stopwatch _sw = new();
    private CancellationTokenSource? _cts;
    public TimeSpan Elapsed => _sw.Elapsed;
    public CancellationToken Token => _cts?.Token ?? CancellationToken.None;
    public void Reset(int total, int concurrency)
    {
        Total = total; Concurrency = concurrency; Created = Updated = Errors = QueryPageCount = 0;
        StopOnError = false; LastError = null; _sw.Reset(); _cts?.Dispose(); _cts = new();
    }
    public void Start() => _sw.Start();
    public void Stop() => _sw.Stop();
    public void Cancel() => _cts?.Cancel();
}
