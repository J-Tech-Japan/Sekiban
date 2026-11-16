using ResultBoxes;
namespace Sekiban.Dcb.Common;

/// <summary>
///     ResultBoxes-typed factory members for <see cref="SortableUniqueId" /> kept for WithResult code.
/// </summary>
public static class SortableUniqueIdResultExtensions
{
    extension(SortableUniqueId)
    {
        /// <summary>
        ///     Create OptionalValue from nullable string
        /// </summary>
        public static OptionalValue<SortableUniqueId> OptionalValue(string? value) =>
            !string.IsNullOrWhiteSpace(value) ? new SortableUniqueId(value) : OptionalValue<SortableUniqueId>.Empty;
    }
}
