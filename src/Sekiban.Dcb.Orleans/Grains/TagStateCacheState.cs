using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     State object for caching tag state in Orleans grain storage
/// </summary>
[GenerateSerializer]
public class TagStateCacheState
{
    [Id(0)]
    public TagState? CachedState { get; set; }
}
