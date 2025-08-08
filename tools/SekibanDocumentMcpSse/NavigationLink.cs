namespace SekibanDocumentMcpSse;

/// <summary>
/// Represents a navigation link in a Markdown document
/// </summary>
public class NavigationLink
{
    public string Title { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
}