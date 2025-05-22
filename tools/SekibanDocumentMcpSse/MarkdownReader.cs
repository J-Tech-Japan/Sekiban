using Markdig;
using System.Text.RegularExpressions;

namespace SekibanDocumentMcpSse;

/// <summary>
/// Utility class for reading and parsing Markdown documents
/// </summary>
public class MarkdownReader
{
    private readonly ILogger<MarkdownReader> _logger;
    public readonly string _docsBasePath;
    
    public MarkdownReader(ILogger<MarkdownReader> logger, string docsBasePath)
    {
        _logger = logger;
        _docsBasePath = docsBasePath;
    }
    
    /// <summary>
    /// Read all markdown files from the docs directory
    /// </summary>
    public async Task<List<MarkdownDocument>> ReadAllDocumentsAsync()
    {
        var documents = new List<MarkdownDocument>();
        try
        {
            if (!Directory.Exists(_docsBasePath))
            {
                _logger.LogWarning("Docs directory not found: {DocsBasePath}", _docsBasePath);
                return documents;
            }
            
            var files = Directory.GetFiles(_docsBasePath, "*.md", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var content = await File.ReadAllTextAsync(file);
                var doc = ParseDocument(fileName, content);
                documents.Add(doc);
            }
            
            // Sort documents by filename (assuming they are numbered)
            documents = documents.OrderBy(d => d.FileName).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading markdown documents from {DocsBasePath}", _docsBasePath);
        }
        
        return documents;
    }
    
    /// <summary>
    /// Parse a single markdown document
    /// </summary>
    private MarkdownDocument ParseDocument(string fileName, string content)
    {
        var document = new MarkdownDocument
        {
            FileName = fileName,
            Content = content
        };
        
        // Extract title (first H1)
        var titleMatch = Regex.Match(content, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (titleMatch.Success)
        {
            document.Title = titleMatch.Groups[1].Value.Trim();
        }
        else
        {
            document.Title = Path.GetFileNameWithoutExtension(fileName);
        }
        
        // Extract code samples
        var codeBlockMatches = Regex.Matches(content, @"```(\w+)\s*\n(.*?)```", RegexOptions.Singleline);
        foreach (Match match in codeBlockMatches)
        {
            var language = match.Groups[1].Value.Trim();
            var code = match.Groups[2].Value;
            
            document.CodeSamples.Add(new CodeSample
            {
                Language = language,
                Code = code,
                Context = ExtractContextBeforeCodeBlock(content, match.Index)
            });
        }
        
        // Extract navigation links
        var navMatches = Regex.Matches(content, @"\>\s*-\s*\[(.*?)\]\((.*?)\)(\s*\((.*?)\))?", RegexOptions.Multiline);
        foreach (Match match in navMatches)
        {
            var title = match.Groups[1].Value.Trim();
            var link = match.Groups[2].Value.Trim();
            var isCurrent = match.Groups[4].Value.Contains("You are here");
            
            document.NavigationLinks.Add(new NavigationLink
            {
                Title = title,
                Link = link,
                IsCurrent = isCurrent
            });
        }
        
        // Extract sections (H2)
        var sectionMatches = Regex.Matches(content, @"^##\s+(.+)$", RegexOptions.Multiline);
        foreach (Match match in sectionMatches)
        {
            var sectionTitle = match.Groups[1].Value.Trim();
            document.Sections.Add(sectionTitle);
        }

        return document;
    }
    
    /// <summary>
    /// Extract context (usually section heading) before a code block
    /// </summary>
    private string ExtractContextBeforeCodeBlock(string content, int codeBlockPosition)
    {
        var contentBeforeBlock = content.Substring(0, codeBlockPosition);
        var headingMatches = Regex.Matches(contentBeforeBlock, @"^(#{1,6})\s+(.+)$", RegexOptions.Multiline);
        
        if (headingMatches.Count > 0)
        {
            var lastHeading = headingMatches[headingMatches.Count - 1];
            return lastHeading.Groups[2].Value.Trim();
        }
        
        return string.Empty;
    }
    
    /// <summary>
    /// Read a specific document by filename
    /// </summary>
    public async Task<MarkdownDocument?> ReadDocumentAsync(string fileName)
    {
        try
        {
            var filePath = Path.Combine(_docsBasePath, fileName);
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Document not found: {FilePath}", filePath);
                return null;
            }
            
            var content = await File.ReadAllTextAsync(filePath);
            return ParseDocument(fileName, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading markdown document {FileName}", fileName);
            return null;
        }
    }
}

/// <summary>
/// Represents a parsed Markdown document
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
    /// Get the document content for the specified section
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

/// <summary>
/// Represents a navigation link in a Markdown document
/// </summary>
public class NavigationLink
{
    public string Title { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
}

/// <summary>
/// Represents a code sample in a Markdown document
/// </summary>
public class CodeSample
{
    public string Language { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}