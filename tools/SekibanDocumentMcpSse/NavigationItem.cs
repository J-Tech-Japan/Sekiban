namespace SekibanDocumentMcpSse;

/// <summary>
///     Navigation item for UI
/// </summary>
public class NavigationItem
{
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public List<NavigationSection> Sections { get; set; } = new();
}
