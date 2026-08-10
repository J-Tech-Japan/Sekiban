namespace Sekiban.Dcb.Commands;

/// <summary>
///     Consistency check DTO: tag string + last SortableUniqueId for reservation. Empty asserts that the tag is empty;
///     null is invalid on the legacy/V1 serialized boundary.
///     Used by WASM clients to specify which tags require consistency checks.
/// </summary>
public record ConsistencyTagEntry(
    string Tag,
    string LastSortableUniqueId);
