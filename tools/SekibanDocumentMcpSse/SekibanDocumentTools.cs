using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace SekibanDocumentMcpSse;

/// <summary>
/// MCP tools for accessing Sekiban documentation
/// </summary>
[McpServerToolType]
public sealed class SekibanDocumentTools
{
    private readonly SekibanDocumentService _documentService;
    private readonly AzureOpenAIService _openAiService;

    /// <summary>
    /// Constructor
    /// </summary>
    public SekibanDocumentTools(SekibanDocumentService documentService, AzureOpenAIService openAiService)
    {
        _documentService = documentService;
        _openAiService = openAiService;
    }

    /// <summary>
    /// Get the navigation structure of the documentation
    /// </summary>
    [McpServerTool, Description("Get the navigation structure of Sekiban documentation.")]
    public async Task<string> GetDocumentNavigation()
    {
        var navigation = await _documentService.GetNavigationAsync();
        return JsonSerializer.Serialize(navigation, SekibanContext.Default.ListNavigationItem);
    }

    /// <summary>
    /// Get a list of all available documents
    /// </summary>
    [McpServerTool, Description("Get a list of all available Sekiban documents.")]
    public async Task<string> GetAllDocuments()
    {
        var documents = await _documentService.GetAllDocumentsAsync();
        return JsonSerializer.Serialize(documents, SekibanContext.Default.ListDocumentInfo);
    }

    /// <summary>
    /// Get a specific document by filename
    /// </summary>
    [McpServerTool, Description("Get a specific Sekiban document by filename.")]
    public async Task<string> GetDocument(
        [Description("The filename of the document (e.g., '01_core_concepts.md')")] string fileName)
    {
        var document = await _documentService.GetDocumentAsync(fileName);
        if (document == null)
        {
            return JsonSerializer.Serialize(new { error = $"Document '{fileName}' not found" });
        }

        return JsonSerializer.Serialize(new
        {
            fileName = document.FileName,
            title = document.Title,
            content = document.Content
        });
    }

    /// <summary>
    /// Get a specific section from a document
    /// </summary>
    [McpServerTool, Description("Get a specific section from a Sekiban document.")]
    public async Task<string> GetDocumentSection(
        [Description("The filename of the document (e.g., '01_core_concepts.md')")] string fileName,
        [Description("The title of the section")] string sectionTitle)
    {
        var section = await _documentService.GetSectionContentAsync(fileName, sectionTitle);
        if (section == null)
        {
            return JsonSerializer.Serialize(new { error = $"Section '{sectionTitle}' not found in document '{fileName}'" });
        }

        return JsonSerializer.Serialize(section, SekibanContext.Default.SectionContent);
    }

    /// <summary>
    /// Search Sekiban documentation
    /// </summary>
    [McpServerTool, Description("Search Sekiban documentation by keyword.")]
    public async Task<string> SearchDocumentation(
        [Description("The search keyword or phrase")] string query)
    {
        var results = await _documentService.SearchAsync(query);
        return JsonSerializer.Serialize(results, SekibanContext.Default.ListSearchResult);
    }

    /// <summary>
    /// Get code samples from Sekiban documentation
    /// </summary>
    [McpServerTool, Description("Get code samples from Sekiban documentation.")]
    public async Task<string> GetCodeSamples(
        [Description("Optional language filter (e.g., 'csharp', 'json')")] string? language = null)
    {
        List<SekibanCodeSample> samples;
        if (string.IsNullOrEmpty(language))
        {
            samples = await _documentService.GetAllCodeSamplesAsync();
        }
        else
        {
            samples = await _documentService.GetCodeSamplesByLanguageAsync(language);
        }
        
        return JsonSerializer.Serialize(samples, SekibanContext.Default.ListSekibanCodeSample);
    }

    /// <summary>
    /// Search for code samples
    /// </summary>
    [McpServerTool, Description("Search for code samples in Sekiban documentation.")]
    public async Task<string> SearchCodeSamples(
        [Description("The search keyword or phrase")] string query)
    {
        var samples = await _documentService.SearchCodeSamplesAsync(query);
        return JsonSerializer.Serialize(samples, SekibanContext.Default.ListSekibanCodeSample);
    }

    /// <summary>
    /// Ask a question about Sekiban and get an answer
    /// </summary>
    [McpServerTool, Description("Ask a question about Sekiban and get an answer using AI.")]
    public async Task<string> AskQuestion(
        [Description("Your question about Sekiban")] string question)
    {
        var answer = await _openAiService.AnswerQuestionAsync(question);
        return JsonSerializer.Serialize(new { question, answer });
    }
}