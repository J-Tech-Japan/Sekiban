namespace SekibanDocumentMcpSse;

/// <summary>
///     Represents a code sample in a Markdown document
/// </summary>
public class CodeSample
{
    public string Language { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}
