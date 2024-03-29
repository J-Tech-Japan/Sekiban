namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     Multi Projection Payload Common Interface
/// </summary>
public interface IMultiProjectionPayloadCommon
{
    /// <summary>
    ///     Aggregate Payload Version:
    ///     This version will be used to identify snapshot type.
    ///     If you update Payload Version, old snapshot will not be used.
    ///     e.g.
    ///     public string GetPayloadVersionIdentifier() => "20230101 1.0.0";
    /// </summary>
    /// <returns>Payload Version</returns>
    public string GetPayloadVersionIdentifier() => "initial";
}
