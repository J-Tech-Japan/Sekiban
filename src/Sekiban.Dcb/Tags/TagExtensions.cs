namespace Sekiban.Dcb.Tags;

/// <summary>
///     Extension methods for ITag interface
/// </summary>
public static class TagExtensions
{
    /// <summary>
    ///     Gets the full tag string in the format "group:content"
    /// </summary>
    public static string GetTag(this ITag tag) => $"{tag.GetTagGroup()}:{tag.GetTagContent()}";
}
