namespace Sekiban.Dcb.Commands;

/// <summary>
///     Consistency check DTO: tag string + last SortableUniqueId for reservation.
///     Used by WASM clients to specify which tags require consistency checks.
/// </summary>
public record ConsistencyTagEntry(
    string Tag,
    string LastSortableUniqueId);
