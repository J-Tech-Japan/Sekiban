using System.Text.Json.Serialization;

namespace SekibanDocumentMcpSse;

public class SekibanDocumentService
{
    private readonly ILogger<SekibanDocumentService> logger;
    private readonly IConfiguration configuration;
    private readonly IHostEnvironment environment;
    private List<DocumentationItem> documentationItems = new();
    private List<CodeSample> codeSamples = new();
    private bool isInitialized = false;

    public SekibanDocumentService(
        ILogger<SekibanDocumentService> logger,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.environment = environment;
    }

    public async Task Initialize()
    {
        if (isInitialized) return;

        try
        {
            // Read documentation from embedded resource or file system
            var docPath = Path.Combine(environment.ContentRootPath, "Documentation", "README_Sekiban_Pure_For_LLM.md");
            if (File.Exists(docPath))
            {
                var content = await File.ReadAllTextAsync(docPath);
                ParseDocumentation(content);
            }
            else
            {
                logger.LogWarning("Documentation file not found at: {DocPath}", docPath);
                // Use embedded documentation as fallback
                var embeddedContent = GetEmbeddedDocumentation();
                ParseDocumentation(embeddedContent);
            }

            isInitialized = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize documentation");
            throw;
        }
    }

    private string GetEmbeddedDocumentation()
    {
        // This would be the documentation content embedded in the assembly
        // For now, we'll just provide a basic content to demonstrate the functionality
        return @"# Sekiban Event Sourcing - LLM Implementation Guide

## Getting Started
Sekiban is a modern event sourcing framework for .NET applications.

## Important Notes
Please use the correct namespaces and follow the recommended project structure.

## Core Concepts
Event Sourcing: Store all state changes as immutable events. Current state is derived by replaying events.";
    }

    private void ParseDocumentation(string content)
    {
        // Parse the markdown content into documentation items and code samples
        // This is a simplified implementation
        var lines = content.Split('\n');
        DocumentationItem? currentItem = null;
        CodeSample? currentSample = null;
        bool inCodeBlock = false;
        string codeBlockContent = string.Empty;
        string codeBlockLanguage = string.Empty;

        foreach (var line in lines)
        {
            if (line.StartsWith("# "))
            {
                // Main title
                if (currentItem != null)
                {
                    documentationItems.Add(currentItem);
                }
                currentItem = new DocumentationItem
                {
                    Title = line.Substring(2).Trim(),
                    Category = "General",
                    Content = ""
                };
            }
            else if (line.StartsWith("## "))
            {
                // Section title
                if (currentItem != null)
                {
                    documentationItems.Add(currentItem);
                }
                currentItem = new DocumentationItem
                {
                    Title = line.Substring(3).Trim(),
                    Category = "Section",
                    Content = ""
                };
            }
            else if (line.StartsWith("### "))
            {
                // Subsection title
                if (currentItem != null)
                {
                    documentationItems.Add(currentItem);
                }
                currentItem = new DocumentationItem
                {
                    Title = line.Substring(4).Trim(),
                    Category = "Subsection",
                    Content = ""
                };
            }
            else if (line.StartsWith("```") && !inCodeBlock)
            {
                // Start of code block
                inCodeBlock = true;
                codeBlockLanguage = line.Substring(3).Trim();
                codeBlockContent = "";
                currentSample = new CodeSample
                {
                    Title = currentItem?.Title ?? "Code Sample",
                    Language = codeBlockLanguage,
                    Code = ""
                };
            }
            else if (line.StartsWith("```") && inCodeBlock)
            {
                // End of code block
                inCodeBlock = false;
                if (currentSample != null)
                {
                    currentSample.Code = codeBlockContent.Trim();
                    codeSamples.Add(currentSample);
                    currentSample = null;
                }
            }
            else if (inCodeBlock)
            {
                // Content of code block
                codeBlockContent += line + "\n";
            }
            else
            {
                // Regular content
                if (currentItem != null)
                {
                    currentItem.Content += line + "\n";
                }
            }
        }

        // Add the last item if there is one
        if (currentItem != null)
        {
            documentationItems.Add(currentItem);
        }
    }

    public async Task<DocumentationItem> GetGeneralDocumentation()
    {
        await Initialize();
        return documentationItems.FirstOrDefault(d => d.Category == "General") ?? 
               new DocumentationItem { Title = "Sekiban Documentation", Category = "General", Content = "Documentation not available." };
    }

    public async Task<DocumentationItem> GetComponentDocumentation(string component)
    {
        await Initialize();
        return documentationItems.FirstOrDefault(d => d.Title.Contains(component, StringComparison.OrdinalIgnoreCase)) ?? 
               new DocumentationItem { Title = component, Category = "Component", Content = $"Documentation for {component} not available." };
    }

    public async Task<CodeSample> GetCodeSample(string feature)
    {
        await Initialize();
        return codeSamples.FirstOrDefault(s => s.Title.Contains(feature, StringComparison.OrdinalIgnoreCase)) ?? 
               new CodeSample { Title = feature, Language = "csharp", Code = $"// Code sample for {feature} not available." };
    }

    public async Task<List<DocumentationItem>> SearchDocumentation(string keyword)
    {
        await Initialize();
        return documentationItems
            .Where(d => d.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) || 
                         d.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

public class DocumentationItem
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class CodeSample
{
    public string Title { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

[JsonSerializable(typeof(List<DocumentationItem>))]
[JsonSerializable(typeof(DocumentationItem))]
[JsonSerializable(typeof(CodeSample))]
[JsonSerializable(typeof(List<CodeSample>))]
internal sealed partial class SekibanContext : JsonSerializerContext { }