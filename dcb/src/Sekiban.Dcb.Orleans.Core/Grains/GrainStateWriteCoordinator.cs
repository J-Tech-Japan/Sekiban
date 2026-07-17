namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Serializes every grain-state write in an activation through a single gate, so a fault-descriptor retry can never
///     overlap a normal snapshot/checkpoint persist and race on the ETag/version. The actual persistence is injected as
///     a delegate, so this is a plain, framework-free component: a friend test can drive it with a parking delegate and
///     prove the serialization DIRECTLY — no Orleans grain, no reliance on turn-based non-reentrancy.
/// </summary>
internal sealed class GrainStateWriteCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<Task> _write;

    public GrainStateWriteCoordinator(Func<Task> write) => _write = write ?? throw new ArgumentNullException(nameof(write));

    /// <summary>
    ///     Runs the injected write under the gate. At most one write is in flight at a time; a second caller waits for
    ///     the first to commit (or fail) before its write begins.
    /// </summary>
    public async Task WriteAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await _write();
        }
        finally
        {
            _gate.Release();
        }
    }
}
