namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     Multi Projection Common Interface
/// </summary>
public interface IMultiProjectionCommon : IProjection
{
    /// <summary>
    ///     Get Payload Version Identifier.
    ///     If this changes, snapshot will not be applied.
    /// </summary>
    /// <returns></returns>
    string GetPayloadVersionIdentifier();
}
