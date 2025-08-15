namespace Sekiban.Dcb.Actors;

/// <summary>
///     Configuration options for GeneralMultiProjection actors
/// </summary>
public class GeneralMultiProjectionActorOptions
{
    /// <summary>
    ///     The safe window time in milliseconds to wait before processing events
    ///     to ensure consistency. Events within this window may arrive out of order.
    ///     Default is 20000 milliseconds (20 seconds).
    /// </summary>
    public int SafeWindowMs { get; set; } = 20000;
}