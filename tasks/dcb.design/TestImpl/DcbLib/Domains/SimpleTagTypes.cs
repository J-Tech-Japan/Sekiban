namespace DcbLib.Domains;

/// <summary>
/// Simple implementation of ITagTypes
/// </summary>
public class SimpleTagTypes : ITagTypes
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Func<string, DcbLib.Tags.ITag>> _tagGroupFactories = new();

    public SimpleTagTypes() { }

    public void RegisterTagGroupType<TTagGroup>() where TTagGroup : DcbLib.Tags.ITagGroup<TTagGroup>
    {
        var groupName = TTagGroup.GetTagGroupName();
        _tagGroupFactories.AddOrUpdate(groupName,
            _ => content => TTagGroup.FromContent(content),
            (_, __) => content => TTagGroup.FromContent(content));
    }

    public DcbLib.Tags.ITag GetTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return new DcbLib.Tags.FallbackTag("", "");
        var parts = tag.Split(':', 2);
        if (parts.Length != 2) return new DcbLib.Tags.FallbackTag("", tag);
        var group = parts[0];
        var content = parts[1];
        if (_tagGroupFactories.TryGetValue(group, out var factory))
        {
            try { return factory(content); }
            catch { return new DcbLib.Tags.FallbackTag(group, content); }
        }
        return new DcbLib.Tags.FallbackTag(group, content);
    }
}