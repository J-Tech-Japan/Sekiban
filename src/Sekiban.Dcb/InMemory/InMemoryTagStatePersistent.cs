using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.InMemory;

/// <summary>
/// In-memory implementation of ITagStatePersistent
/// Stores state in memory for the lifetime of the actor
/// </summary>
public class InMemoryTagStatePersistent : ITagStatePersistent
{
    private TagState? _cachedState;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    
    public async Task<TagState?> LoadStateAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            return _cachedState;
        }
        finally
        {
            _stateLock.Release();
        }
    }
    
    public async Task SaveStateAsync(TagState state)
    {
        await _stateLock.WaitAsync();
        try
        {
            _cachedState = state;
        }
        finally
        {
            _stateLock.Release();
        }
    }
    
    public async Task ClearStateAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            _cachedState = null;
        }
        finally
        {
            _stateLock.Release();
        }
    }
}