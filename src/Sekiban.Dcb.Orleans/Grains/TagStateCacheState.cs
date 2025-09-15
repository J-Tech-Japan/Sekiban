using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     State object for caching tag state in Orleans grain storage
///     Uses SerializableTagState to avoid interface serialization issues
/// </summary>
[GenerateSerializer]
public class TagStateCacheState
{
    [Id(0)]
    public SerializableTagState? CachedState { get; set; }
}
