using Sekiban.Dcb.Tags;
using System.Collections.Concurrent;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     AOT-compatible implementation of ITagTypes.
/// </summary>
public class AotTagTypes : ITagTypes
{
    private readonly ConcurrentDictionary<string, Func<string, ITag>> _tagGroupFactories = new();

    /// <inheritdoc />
    public ITag GetTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return new FallbackTag("", "");
        var parts = tag.Split(':', 2);
        if (parts.Length != 2) return new FallbackTag("", tag);
        var group = parts[0];
        var content = parts[1];
        if (_tagGroupFactories.TryGetValue(group, out var factory))
        {
            try { return factory(content); }
            catch { return new FallbackTag(group, content); }
        }
        return new FallbackTag(group, content);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllTagGroupNames() => _tagGroupFactories.Keys.ToList();

    /// <summary>
    ///     Register a tag group type.
    /// </summary>
    /// <typeparam name="TTagGroup">The tag group type to register</typeparam>
    public void RegisterTagGroupType<TTagGroup>() where TTagGroup : ITagGroup<TTagGroup>
    {
        var groupName = TTagGroup.TagGroupName;

        if (!_tagGroupFactories.TryAdd(groupName, content => TTagGroup.FromContent(content)))
        {
            throw new InvalidOperationException($"Tag group already registered: {groupName}");
        }
    }
}
