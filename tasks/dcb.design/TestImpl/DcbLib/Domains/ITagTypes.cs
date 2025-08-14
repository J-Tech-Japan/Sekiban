namespace DcbLib.Domains;

/// <summary>
/// Interface for managing tag types in the domain
/// </summary>
public interface ITagTypes
{
	/// <summary>
	/// 文字列表現 (group:content) からタグを復元します。
	/// 未登録時や失敗時は FallbackTag を返します。
	/// </summary>
	DcbLib.Tags.ITag GetTag(string tag);
}