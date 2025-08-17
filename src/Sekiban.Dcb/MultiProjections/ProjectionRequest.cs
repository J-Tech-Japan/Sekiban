namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Projection request from handler
/// </summary>
public record ProjectionRequest<T>(
    Guid ItemId,
    Func<T?, T> Projector // Transform function (null input means create new)
) where T : class;
