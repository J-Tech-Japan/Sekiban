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