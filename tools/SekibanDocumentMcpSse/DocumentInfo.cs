namespace SekibanDocumentMcpSse;

/// <summary>
///     Basic information about a document
/// </summary>
public class DocumentInfo
{
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> Sections { get; set; } = new();
}
