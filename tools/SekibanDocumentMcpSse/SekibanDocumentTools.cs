using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace SekibanDocumentMcpSse;

[McpServerToolType]
public sealed class SekibanDocumentTools
{
    private readonly SekibanDocumentService documentService;

    public SekibanDocumentTools(SekibanDocumentService documentService)
    {
        this.documentService = documentService;
    }

    [McpServerTool, Description("Get general Sekiban documentation.")]
    public async Task<string> GetSekibanDocumentation()
    {
        var documentation = await documentService.GetGeneralDocumentation();
        return JsonSerializer.Serialize(documentation, SekibanContext.Default.DocumentationItem);
    }

    [McpServerTool, Description("Get documentation on a specific Sekiban component.")]
    public async Task<string> GetComponentDocumentation([Description("The name of the component (e.g., 'Commands', 'Events', 'Projectors')")] string component)
    {
        var documentation = await documentService.GetComponentDocumentation(component);
        return JsonSerializer.Serialize(documentation, SekibanContext.Default.DocumentationItem);
    }

    [McpServerTool, Description("Get code sample for a specific Sekiban feature.")]
    public async Task<string> GetSekibanCodeSample([Description("The feature you want a code sample for")] string feature)
    {
        var sample = await documentService.GetCodeSample(feature);
        return JsonSerializer.Serialize(sample, SekibanContext.Default.CodeSample);
    }

    [McpServerTool, Description("Search Sekiban documentation by keyword.")]
    public async Task<string> SearchDocumentation([Description("The search keyword or phrase")] string keyword)
    {
        var results = await documentService.SearchDocumentation(keyword);
        return JsonSerializer.Serialize(results, SekibanContext.Default.ListDocumentationItem);
    }
}