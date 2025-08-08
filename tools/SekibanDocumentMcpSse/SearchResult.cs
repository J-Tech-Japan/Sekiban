namespace SekibanDocumentMcpSse;

/// <summary>
/// Search result
/// </summary>
public class SearchResult
{
    public string DocumentTitle { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool MatchedInTitle { get; set; }
    public List<string> MatchedSections { get; set; } = new();
}