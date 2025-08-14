namespace Sekiban.Dcb.Domains;

/// <summary>
///     Interface for managing tag types in the domain
/// </summary>
public interface ITagTypes
{
	/// <summary>
	/// 文字列表現 (group:content) から適切な <see cref="Sekiban.Dcb.Tags.ITag"/> インスタンスを復元します。
	/// 未登録グループまたはパース失敗時は <see cref="Sekiban.Dcb.Tags.FallbackTag"/> を返します。
	/// </summary>
	/// <param name="tag">group:content 形式の文字列。</param>
	/// <returns>復元されたタグ、またはフォールバックタグ。</returns>
	Sekiban.Dcb.Tags.ITag GetTag(string tag);
}
