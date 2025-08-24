using System.Text.RegularExpressions;
namespace SekibanDocumentMcpSse;

/// <summary>
///     Represents a parsed Markdown document
/// </summary>
public class MarkdownDocument
{
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Sections { get; set; } = new();
    public List<NavigationLink> NavigationLinks { get; set; } = new();
    public List<CodeSample> CodeSamples { get; set; } = new();

    /// <summary>
    ///     Get the document content for the specified section
    /// </summary>
    public string GetSectionContent(string sectionTitle)
    {
        var pattern = $@"^## {Regex.Escape(sectionTitle)}$(.*?)(?:^## |\z)";
        var match = Regex.Match(Content, pattern, RegexOptions.Multiline | RegexOptions.Singleline);

        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return string.Empty;
    }
}
