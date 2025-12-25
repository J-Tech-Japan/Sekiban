using ResultBoxes;
using Sekiban.Pure.Events;
namespace DaprSample.Api;

public class PatchedEventReader : IEventReader
{
    private readonly ILogger<PatchedEventReader> _logger;

    public PatchedEventReader(ILogger<PatchedEventReader> logger) => _logger = logger;

    public Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo retrievalInfo)
    {
        _logger.LogInformation("PatchedEventReader.GetEvents called");

        // Always return empty list to avoid timeout
        return Task.FromResult(ResultBox<IReadOnlyList<IEvent>>.FromValue(new List<IEvent>()));
    }
}
