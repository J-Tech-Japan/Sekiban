using System.Collections.Concurrent;
using System.Reflection;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Domains;

/// <summary>
/// ITagTypes のシンプル実装です。
/// </summary>
public class SimpleTagTypes : ITagTypes
{
	private readonly ConcurrentDictionary<string, Func<string, ITag>> _tagGroupFactories = new();

	/// <summary>
	/// ITagGroup 実装型を登録します。
	/// </summary>
	public void RegisterTagGroupType<TTagGroup>() where TTagGroup : ITagGroup<TTagGroup>
	{
		var groupName = TTagGroup.GetTagGroupName();
		_tagGroupFactories.AddOrUpdate(groupName,
			_ => content => TTagGroup.FromContent(content),
			(_, __) => content => TTagGroup.FromContent(content));
	}

	/// <summary>
	/// 文字列表現 (group:content) から適切なタグインスタンスを取得します。
	/// </summary>
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
}
