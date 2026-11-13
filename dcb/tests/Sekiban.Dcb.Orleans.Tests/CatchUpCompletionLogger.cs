using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Orleans.Grains;

namespace Sekiban.Dcb.Orleans.Tests;

// Capture the completion decision itself: unchanged-checkpoint skipping can hide a persist attempt from store counters.
internal sealed class CatchUpCompletionLogger : ILogger<MultiProjectionGrain>, ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, EventId EventId, IReadOnlyDictionary<string, object?> Fields)> Rebuilds { get; } = new();
    private readonly ConcurrentQueue<string> _reasons = new();
    public IReadOnlyList<string> Reasons => _reasons.ToArray();
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Dispose() { }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (eventId == MultiProjectionLogEvents.RuntimeFullRebuildRequired &&
            state is IEnumerable<KeyValuePair<string, object?>> rebuildFields)
            Rebuilds.Enqueue((logLevel, eventId, rebuildFields.ToDictionary(pair => pair.Key, pair => pair.Value)));
        if (eventId.Name != "CatchUpCompleted" || state is not IEnumerable<KeyValuePair<string, object?>> fields)
            return;
        var reason = fields.Single(field => field.Key == "PersistReason").Value;
        _reasons.Enqueue((string)reason!);
    }
}
