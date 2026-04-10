using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;

namespace SekibanDcbDecider.ApiService;

sealed class CompatibleMemoryGrainStorage : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>, IDisposable
{
    private readonly MemoryGrainStorage _inner;

    public CompatibleMemoryGrainStorage(MemoryGrainStorage inner)
    {
        _inner = inner;
    }

    Task IGrainStorage.ReadStateAsync<T>(
        string stateName,
        GrainId grainId,
        IGrainState<T> grainState) =>
        _inner.ReadStateAsync(stateName, grainId, grainState);

    Task IGrainStorage.WriteStateAsync<T>(
        string stateName,
        GrainId grainId,
        IGrainState<T> grainState) =>
        _inner.WriteStateAsync(stateName, grainId, grainState);

    Task IGrainStorage.ClearStateAsync<T>(
        string stateName,
        GrainId grainId,
        IGrainState<T> grainState) =>
        _inner.ClearStateAsync(stateName, grainId, grainState);

    public void Participate(ISiloLifecycle lifecycle)
    {
        if (_inner is ILifecycleParticipant<ISiloLifecycle> participant)
        {
            participant.Participate(lifecycle);
        }
    }

    public void Dispose()
    {
        _inner.Dispose();
    }
}

readonly record struct PostgresBootstrapSettings(
    string DatabaseName,
    string EscapedDatabaseName,
    string AdminConnectionString);
