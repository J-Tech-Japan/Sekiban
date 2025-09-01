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
      document.getElementById('canceled').textContent = s.canceled ? 'Yes' : 'No';
      document.getElementById('weatherCount').textContent = s.weatherCount !== null && s.weatherCount !== undefined ? s.weatherCount : '-';
      document.getElementById('lastError').textContent = s.lastError ?? '';
      
      // Show processing indicator if weather count is still updating after benchmark completion
  if (!s.isRunning && s.weatherCount !== null && s.weatherCount < s.created) {{
        document.getElementById('weatherCount').textContent = s.weatherCount + ' (処理中...)';
      }}
      
      // Update last update timestamp
      const now = new Date().toLocaleTimeString();
      document.getElementById('lastUpdate').textContent = now;
      
      // Update event statistics if available
      if (s.eventStats) {{
        document.getElementById('uniqueEvents').textContent = s.eventStats.totalUniqueEvents;
        document.getElementById('totalDeliveries').textContent = s.eventStats.totalDeliveries;
        document.getElementById('duplicateDeliveries').textContent = s.eventStats.duplicateDeliveries;
        document.getElementById('maxDeliveryCount').textContent = s.eventStats.maxDeliveryCount;
        if (typeof s.eventStats.streamUniqueEvents !== 'undefined') {{
          document.getElementById('streamUnique').textContent = s.eventStats.streamUniqueEvents;
          document.getElementById('streamDeliveries').textContent = s.eventStats.streamDeliveries;
        }}
        if (typeof s.eventStats.catchUpUniqueEvents !== 'undefined') {{
          document.getElementById('catchupUnique').textContent = s.eventStats.catchUpUniqueEvents;
          document.getElementById('catchupDeliveries').textContent = s.eventStats.catchUpDeliveries;
        }}
      }}
    }}

    async function startRun() {{
      const total = document.getElementById('total').value || 10000;
      const conc = document.getElementById('conc').value || 32;
      const stopOnError = document.getElementById('stopOnError').checked ? 'true' : 'false';
      const mode = document.getElementById('endpointMode').value;
      const btn = document.getElementById('startBtn');
      btn.disabled = true;
      try {{
        const r = await fetch(`/run?total=${{total}}&concurrency=${{conc}}&stopOnError=${{stopOnError}}&mode=${{mode}}`, {{ method: 'POST' }});
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

    // Dynamic interval for status updates
    let statusInterval = setInterval(fetchStatus, 1000);
    let lastWeatherCount = 0;
    let unchangedCounter = 0;
    
    // Auto-adjust polling interval based on activity
    setInterval(() => {{
      fetch('/status').then(r => r.json()).then(s => {{
        if (!s.isRunning && s.weatherCount !== null) {{
          if (s.weatherCount === lastWeatherCount) {{
            unchangedCounter++;
            // If count hasn't changed for 30 seconds, slow down polling to every 5 seconds
            if (unchangedCounter > 30) {{
              clearInterval(statusInterval);
              statusInterval = setInterval(fetchStatus, 5000);
            }}
          }} else {{
            unchangedCounter = 0;
            lastWeatherCount = s.weatherCount;
            // If count is changing, ensure we're polling every second
            clearInterval(statusInterval);
            statusInterval = setInterval(fetchStatus, 1000);
          }}
        }}
      }});
    }}, 1000);
    
    window.addEventListener('load', fetchStatus);
  </script>
</head>
<body>
  <h1>Bench Runner</h1>
  <p>API: <code>{System.Net.WebUtility.HtmlEncode(Environment.GetEnvironmentVariable("ApiBaseUrl") ?? "(not set)")}</code></p>
  <div class='row'>
    <label>Total <input id='total' type='number' min='1' value='10000' /></label>
    <label>Concurrency <input id='conc' type='number' min='1' value='32' /></label>
    <label><input id='stopOnError' type='checkbox' checked /> Stop on first error</label>
    <label>Endpoint
      <select id='endpointMode'>
        <option value='standard'>/weatherforecast</option>
        <option value='single'>/weatherforecastsingle</option>
        <option value='generic'>/weatherforecastgeneric</option>
      </select>
    </label>
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
  <div class='row'>
    <div>停止(エラー): <strong id='canceled'>No</strong></div>
    <div>Weather総数: <strong id='weatherCount'>-</strong></div>
    <div>LastError: <strong id='lastError'></strong></div>
  </div>
  <div class='row'>
    <div>最終更新: <strong id='lastUpdate'>-</strong></div>
  </div>
  <div class='row' style='background:#f0f0f0; padding:0.5rem; margin-top:0.5rem'>
    <div>ユニークイベント: <strong id='uniqueEvents'>-</strong></div>
    <div>総配信数: <strong id='totalDeliveries'>-</strong></div>
    <div>重複配信: <strong id='duplicateDeliveries'>-</strong></div>
    <div>最大配信回数: <strong id='maxDeliveryCount'>-</strong></div>
  </div>
  <div class='row' style='background:#f7f7f7; padding:0.5rem; margin-top:0.25rem'>
    <div>Stream ユニーク: <strong id='streamUnique'>-</strong></div>
    <div>Stream 配信数: <strong id='streamDeliveries'>-</strong></div>
    <div>CatchUp ユニーク: <strong id='catchupUnique'>-</strong></div>
    <div>CatchUp 配信数: <strong id='catchupDeliveries'>-</strong></div>
  </div>
  <h3>ログ</h3>
  <div id='log'></div>
  <p style='margin-top:1rem'>
    エンドポイント: <code>POST /run?total=&lt;int&gt;&amp;concurrency=&lt;int&gt;</code>, <code>GET /status</code>
  </p>

  <h3>Projection 状況</h3>
  <div class='row' style='margin:.25rem 0'>
    <label><input type='checkbox' id='snapshotUnsafe' checked/> Snapshot unsafe</label>
  </div>
  <div style='display:grid; grid-template-columns: 160px 1fr 1fr 1fr 1fr 1fr 1fr; gap:.5rem; align-items:center;'>
    <div></div>
    <div><strong>Count (実行)</strong></div>
    <div><strong>Status (非実行)</strong></div>
    <div><strong>Persist</strong></div>
    <div><strong>Deactivate</strong></div>
    <div><strong>Snapshot</strong></div>
    <div><strong>Refresh</strong></div>
    <div>standard</div>
    <div><button onclick='loadCount(&quot;standard&quot;)'>fetch</button> <code id='count-standard'>-</code></div>
    <div><button onclick='loadStatus(&quot;standard&quot;)'>fetch</button> <code id='status-standard'>-</code></div>
    <div><button onclick='doPersist(&quot;standard&quot;)'>persist</button></div>
    <div><button onclick='doDeactivate(&quot;standard&quot;)'>deactivate</button></div>
    <div><button onclick='doSnapshot(&quot;standard&quot;)'>snapshot</button></div>
    <div><button onclick='doRefresh(&quot;standard&quot;)'>refresh</button></div>
    <div>single</div>
    <div><button onclick='loadCount(&quot;single&quot;)'>fetch</button> <code id='count-single'>-</code></div>
    <div><button onclick='loadStatus(&quot;single&quot;)'>fetch</button> <code id='status-single'>-</code></div>
    <div><button onclick='doPersist(&quot;single&quot;)'>persist</button></div>
    <div><button onclick='doDeactivate(&quot;single&quot;)'>deactivate</button></div>
    <div><button onclick='doSnapshot(&quot;single&quot;)'>snapshot</button></div>
    <div><button onclick='doRefresh(&quot;single&quot;)'>refresh</button></div>
    <div>generic</div>
    <div><button onclick='loadCount(&quot;generic&quot;)'>fetch</button> <code id='count-generic'>-</code></div>
    <div><button onclick='loadStatus(&quot;generic&quot;)'>fetch</button> <code id='status-generic'>-</code></div>
    <div><button onclick='doPersist(&quot;generic&quot;)'>persist</button></div>
    <div><button onclick='doDeactivate(&quot;generic&quot;)'>deactivate</button></div>
    <div><button onclick='doSnapshot(&quot;generic&quot;)'>snapshot</button></div>
    <div><button onclick='doRefresh(&quot;generic&quot;)'>refresh</button></div>
  </div>
  <script>
    async function loadCount(mode) {{
      const r = await fetch('/projection/count?mode=' + mode);
      const j = await r.json();
      const id = 'count-' + mode;
      if(j.error) {{ document.getElementById(id).textContent = 'error: ' + j.error; return; }}
  document.getElementById(id).textContent = 'safeVersion:' + j.safeVersion + ' unsafeVersion:' + (j.unsafeVersion ?? '-') ;
  document.getElementById(id).textContent = 'safeVersion:' + j.safeVersion + ' unsafeVersion:' + (j.unsafeVersion ?? '-') + ' totalCount:' + (j.totalCount ?? '-') ;
    }}
    async function loadStatus(mode) {{
      const r = await fetch('/projection/status?mode=' + mode);
      const j = await r.json();
      const id = 'status-' + mode;
      if(j.error) {{ document.getElementById(id).textContent = 'error: ' + j.error; return; }}
      // まだプロジェクション未構築の可能性を表示
      const notInit = (j.stateSize===0 && !j.isSubscriptionActive && !j.isCaughtUp);
      if (notInit) {{
        document.getElementById(id).textContent = 'not projected yet';
      }} else {{
        const safeSize = (typeof j.safeStateSize !== 'undefined') ? (' safeSize:' + j.safeStateSize) : '';
        const unsafeSize = (typeof j.unsafeStateSize !== 'undefined') ? (' unsafeSize:' + j.unsafeStateSize) : '';
        document.getElementById(id).textContent = 'caughtUp:' + j.isCaughtUp + ' pos:' + (j.currentPosition ?? '') + ' size:' + j.stateSize + safeSize + unsafeSize + ' events:' + j.eventsProcessed;
      }}
    }}

    async function doPersist(mode) {{
      try {{
        const r = await fetch('/projection/persist?mode=' + mode, {{ method: 'POST' }});
        const t = await r.text();
        log(`Persist(${{mode}}) => ${{r.status}} ${{t}}`);
      }} catch (e) {{
        log(`Persist(${{mode}}) failed: ` + e);
      }}
    }}

    async function doDeactivate(mode) {{
      try {{
        const r = await fetch('/projection/deactivate?mode=' + mode, {{ method: 'POST' }});
        const t = await r.text();
        log(`Deactivate(${{mode}}) => ${{r.status}} ${{t}}`);
      }} catch (e) {{
        log(`Deactivate(${{mode}}) failed: ` + e);
      }}
    }}

    async function doSnapshot(mode) {{
      try {{
        const unsafeState = document.getElementById('snapshotUnsafe').checked ? 'true' : 'false';
        const r = await fetch('/projection/snapshot?mode=' + mode + '&unsafeState=' + unsafeState);
        const txt = await r.text();
        if (!r.ok) {{ log(`Snapshot(${{mode}}) => ${{r.status}} ${{txt}}`); return; }}
        try {{
          const j = JSON.parse(txt);
          const off = j.IsOffloaded === true;
          const ver = off ? j.OffloadedState?.Version : j.InlineState?.Version;
          const len = off ? (j.OffloadedState?.PayloadLength ?? 0) : ((j.InlineState?.Payload ?? '').length ?? 0);
          log(`Snapshot(${{mode}}) ok: offloaded=${{off}}, version=${{ver}}, payloadLength=${{len}}`);
        }} catch {{
          log(`Snapshot(${{mode}}) ok: length=${{txt.length}}`);
        }}
      }} catch (e) {{
        log(`Snapshot(${{mode}}) failed: ` + e);
      }}
    }}

    async function doRefresh(mode) {{
      try {{
        const r = await fetch('/projection/refresh?mode=' + mode, {{ method: 'POST' }});
        const t = await r.text();
        log(`Refresh(${{mode}}) => ${{r.status}} ${{t}}`);
      }} catch (e) {{
        log(`Refresh(${{mode}}) failed: ` + e);
      }}
    }}
  </script>
</body>
</html>
", "text/html"));

app.MapGet("/status", async () =>
{
    // Before start, do NOT access API endpoints to avoid activating grains
    int? weatherCount = null;
    EventDeliveryStatistics? eventStats = null;
    if ((state.IsRunning || state.HasRun) && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ApiBaseUrl")))
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("ApiBaseUrl")!.TrimEnd('/')) };

            // Get event delivery statistics only (does not execute projection)
            var mode = state.Mode ?? (state.UseSingle ? "single" : "standard");
            var statsPath = mode == "single"
                ? "/api/weatherforecastsingle/event-statistics"
                : mode == "generic" ? "/api/weatherforecastgeneric/event-statistics" : "/api/weatherforecast/event-statistics";
            var statsResponse = await http.GetAsync(statsPath);
            if (statsResponse.IsSuccessStatusCode)
            {
                var json = await statsResponse.Content.ReadAsStringAsync();
                eventStats = JsonSerializer.Deserialize<EventDeliveryStatistics>(json);
            }

            // Throttle count fetch to reduce duplicate deliveries from frequent catch-ups
            var now = DateTime.UtcNow;
            var cache = state.GetOrCreateCountCache(mode);
            var minInterval = state.IsRunning ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(10);
      if (cache.LastFetchedUtc == null || now - cache.LastFetchedUtc > minInterval)
            {
                var countPath = mode == "single"
                    ? "/api/weatherforecastsingle/count"
                    : mode == "generic" ? "/api/weatherforecastgeneric/count" : "/api/weatherforecast/count";
                var countResponse = await http.GetAsync(countPath);
                if (countResponse.IsSuccessStatusCode)
                {
                    var json = await countResponse.Content.ReadAsStringAsync();
                    var count = JsonSerializer.Deserialize<WeatherCountResponse>(json);
        cache.Value = count?.totalCount;
                    cache.LastFetchedUtc = now;
                }
                // On failure, keep previous cached value
            }
            weatherCount = cache.Value;
        }
        catch { /* Ignore errors */ }
    }

    return Results.Json(new
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
        state.LastError,
        WeatherCount = weatherCount, // null means "not fetched"; use Projection 状況の Count(fetch) で取得
        EventStats = eventStats,
        EndpointMode = state.Mode ?? (state.UseSingle ? "single" : "standard")
    });
});

app.MapPost("/run", async (int? total, int? concurrency, bool? stopOnError, string? mode) =>
{
    if (state.IsRunning) return Results.BadRequest(new { message = "Already running" });

    var apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl")?.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(apiBase))
        return Results.BadRequest(new { message = "ApiBaseUrl env is not set" });

    state.Reset(total ?? GetEnvInt("BENCH_TOTAL", 10000), concurrency ?? GetEnvInt("BENCH_CONCURRENCY", 32));
    state.Mode = (mode ?? (state.UseSingle ? "single" : "standard")).ToLowerInvariant();
    state.StopOnError = stopOnError ?? false;
    state.HasRun = true;

    // Kick off a query ONLY for the selected mode to start its subscription
    // Use the Count endpoint so it aligns with the UI’s projection section
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(apiBase!) };
        var countPath = state.Mode switch
        {
            "single" => "/api/weatherforecastsingle/count",
            "generic" => "/api/weatherforecastgeneric/count",
            _ => "/api/weatherforecast/count"
        };
        var _ = await http.GetAsync(countPath);
    }
    catch { /* ignore preflight count error */ }

    _ = Task.Run(() => RunAsync(apiBase!, state));
    return Results.Accepted($"/status", new { message = "Started", state.Total, state.Concurrency, state.StopOnError });
});

// Projection count (triggers projection)
app.MapGet("/projection/count", async (string? mode) =>
{
    var apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl")?.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.BadRequest(new { error = "ApiBaseUrl not set" });
    var m = (mode ?? "standard").ToLowerInvariant();
    var path = m=="single"? "/api/weatherforecastsingle/count" : m=="generic"? "/api/weatherforecastgeneric/count" : "/api/weatherforecast/count";
    try{
        using var http = new HttpClient{ BaseAddress = new Uri(apiBase!) };
        var res = await http.GetAsync(path);
        if(!res.IsSuccessStatusCode) return Results.BadRequest(new { error = await res.Content.ReadAsStringAsync() });
        var json = await res.Content.ReadAsStringAsync();
        return Results.Text(json, "application/json");
    } catch(Exception ex){
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Projection status (does not execute projection)
app.MapGet("/projection/status", async (string? mode) =>
{
    var apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl")?.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.BadRequest(new { error = "ApiBaseUrl not set" });
    var m = (mode ?? "standard").ToLowerInvariant();
    var path = m=="single"? "/api/weatherforecastsingle/status" : m=="generic"? "/api/weatherforecastgeneric/status" : "/api/weatherforecast/status";
    try{
        using var http = new HttpClient{ BaseAddress = new Uri(apiBase!) };
        var res = await http.GetAsync(path);
        if(!res.IsSuccessStatusCode) return Results.BadRequest(new { error = await res.Content.ReadAsStringAsync() });
        var json = await res.Content.ReadAsStringAsync();
        return Results.Text(json, "application/json");
    } catch(Exception ex){
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Projection control: persist state
app.MapPost("/projection/persist", async (string? mode) =>
{
    var apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl")?.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.BadRequest(new { error = "ApiBaseUrl not set" });
    var m = (mode ?? "standard").ToLowerInvariant();
    var name = GetProjectorName(m);
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(apiBase!) };
        var res = await http.PostAsync($"/api/projections/persist?name={Uri.EscapeDataString(name)}", null);
        var json = await Helpers.SafeReadAsync(res, CancellationToken.None);
        if (!res.IsSuccessStatusCode) return Results.BadRequest(new { error = json });
        return Results.Text(json, "application/json");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Projection control: deactivate grain
app.MapPost("/projection/deactivate", async (string? mode) =>
{
    var apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl")?.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.BadRequest(new { error = "ApiBaseUrl not set" });
    var m = (mode ?? "standard").ToLowerInvariant();
    var name = GetProjectorName(m);
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(apiBase!) };
        var res = await http.PostAsync($"/api/projections/deactivate?name={Uri.EscapeDataString(name)}", null);
        var json = await Helpers.SafeReadAsync(res, CancellationToken.None);
        if (!res.IsSuccessStatusCode) return Results.BadRequest(new { error = json });
        return Results.Text(json, "application/json");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Projection control: snapshot fetch
app.MapGet("/projection/snapshot", async (string? mode, bool? unsafeState) =>
{
    var apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl")?.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.BadRequest(new { error = "ApiBaseUrl not set" });
    var m = (mode ?? "standard").ToLowerInvariant();
    var name = GetProjectorName(m);
    var unsafeFlag = unsafeState ?? true;
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(apiBase!) };
        var res = await http.GetAsync($"/api/projections/snapshot?name={Uri.EscapeDataString(name)}&unsafeState={(unsafeFlag ? "true" : "false")}");
        var txt = await Helpers.SafeReadAsync(res, CancellationToken.None);
        if (!res.IsSuccessStatusCode) return Results.BadRequest(new { error = txt });
        return Results.Text(txt, "application/json");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Projection control: refresh (catch-up)
app.MapPost("/projection/refresh", async (string? mode) =>
{
    var apiBase = Environment.GetEnvironmentVariable("ApiBaseUrl")?.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.BadRequest(new { error = "ApiBaseUrl not set" });
    var m = (mode ?? "standard").ToLowerInvariant();
    var name = GetProjectorName(m);
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(apiBase!) };
        var res = await http.PostAsync($"/api/projections/refresh?name={Uri.EscapeDataString(name)}", null);
        var json = await Helpers.SafeReadAsync(res, CancellationToken.None);
        if (!res.IsSuccessStatusCode) return Results.BadRequest(new { error = json });
        return Results.Text(json, "application/json");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();
return;

static int GetEnvInt(string name, int def)
    => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : def;

static string GetProjectorName(string mode)
    => mode == "single" ? "WeatherForecastProjectorWithTagStateProjector"
       : mode == "generic" ? "GenericTagMultiProjector_WeatherForecastProjector_WeatherForecast"
       : "WeatherForecastProjection";

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
                            var body = await Helpers.SafeReadAsync(res, token);
                            state.LastError = $"Create failed: {(int)res.StatusCode} {res.ReasonPhrase} - {Helpers.Trim(body)}";
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
                            var body = await Helpers.SafeReadAsync(upd, token);
                            state.LastError = $"Update failed: {(int)upd.StatusCode} {upd.ReasonPhrase} - {Helpers.Trim(body)}";
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
        var mode2 = state.Mode ?? (state.UseSingle ? "single" : "standard");
        var listPath = mode2 == "single"
            ? "/api/weatherforecastsingle"
            : mode2 == "generic" ? "/api/weatherforecastgeneric" : "/api/weatherforecast";
        var listRes = await http.GetAsync($"{listPath}?pageNumber=1&pageSize=100");
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
    public bool UseSingle;
    public string? Mode;
    public bool HasRun;
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

    private readonly Dictionary<string, CountCache> _countCache = new(StringComparer.OrdinalIgnoreCase);
    public CountCache GetOrCreateCountCache(string mode)
    {
        if (!_countCache.TryGetValue(mode, out var c))
        {
            c = new CountCache();
            _countCache[mode] = c;
        }
        return c;
    }
}

sealed class CountCache
{
    public int? Value { get; set; }
    public DateTime? LastFetchedUtc { get; set; }
}

// Helper class for deserializing weather count response
public class WeatherCountResponse
{
  public int safeVersion { get; set; }
  public int unsafeVersion { get; set; }
  public int totalCount { get; set; }
}

// Helper class for deserializing event delivery statistics
public class EventDeliveryStatistics
{
    public int totalUniqueEvents { get; set; }
    public long totalDeliveries { get; set; }
    public long duplicateDeliveries { get; set; }
    public int eventsWithMultipleDeliveries { get; set; }
    public int maxDeliveryCount { get; set; }
    public double averageDeliveryCount { get; set; }
    public int? streamUniqueEvents { get; set; }
    public long? streamDeliveries { get; set; }
    public int? catchUpUniqueEvents { get; set; }
    public long? catchUpDeliveries { get; set; }
}

static class Helpers
{
    public static async Task<string> SafeReadAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try { return await res.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }
    public static string Trim(string s)
        => string.IsNullOrEmpty(s) ? s : (s.Length > 256 ? s.Substring(0, 256) + "..." : s);
}
