using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.TagStateCancellation.Legacy1018Fixture;

/// <summary>
///     A real binary compiled before SEK-G55. It intentionally implements only the original interface members.
/// </summary>
public sealed class Legacy1018TagStateActor : ITagStateActorCommon
{
    public int GetStateCalls { get; private set; }

    public Task<SerializableTagState> GetStateAsync()
    {
        GetStateCalls++;
        return Task.FromResult(
            new SerializableTagState(
                Array.Empty<byte>(),
                0,
                string.Empty,
                "Legacy",
                "binary",
                "Projector",
                nameof(EmptyTagStatePayload),
                string.Empty,
                nameof(EmptyTagStatePayload)));
    }

    public Task<string> GetTagStateActorIdAsync() => Task.FromResult("Legacy:binary:Projector");
}

/// <summary>
///     A pre-SEK-G55 client call-site. Its assembly reference has no knowledge of the cancellation overloads, so this
///     call proves the unchanged token-less grain method remains dispatchable against the new silo.
/// </summary>
public static class Legacy1018TagStateGrainClient
{
    public static Task<SerializableTagState> GetStateAsync(ITagStateGrain grain) => grain.GetStateAsync();
}
