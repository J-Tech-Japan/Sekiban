using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
namespace SekibanDocumentMcpSse;

/// <summary>
///     Service for interacting with Azure OpenAI
/// </summary>
public class AzureOpenAIService
{
    private readonly AzureOpenAIClient? _client;
    private readonly SekibanDocumentService _documentService;
    private readonly ILogger<AzureOpenAIService> _logger;
    private readonly AzureOpenAIOptions _options;

    /// <summary>
    ///     Constructor
    /// </summary>
    public AzureOpenAIService(
        ILogger<AzureOpenAIService> logger,
        IOptions<AzureOpenAIOptions> options,
        SekibanDocumentService documentService)
    {
        _logger = logger;
        _options = options.Value;
        _documentService = documentService;

        if (!string.IsNullOrEmpty(_options.Endpoint) && !string.IsNullOrEmpty(_options.ApiKey))
        {
            try
            {
                _client = new AzureOpenAIClient(
                    new Uri(_options.Endpoint),
                    new ApiKeyCredential(_options.ApiKey),
                    new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_12_01_Preview));
                _logger.LogInformation("Azure OpenAI client initialized");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure OpenAI client");
            }
        } else
        {
            _logger.LogWarning("Azure OpenAI settings are missing. Question answering will not be available.");
        }
    }

    /// <summary>
    ///     Answer a question about Sekiban using Azure OpenAI
    /// </summary>
    public async Task<string> AnswerQuestionAsync(string question, string documentSet)
    {
        if (_client == null || string.IsNullOrEmpty(_options.DeploymentName))
        {
            return
                "I'm sorry, but Azure OpenAI is not configured properly. Please add valid Azure OpenAI settings to appsettings.json.";
        }

        try
        {
            var context = await BuildQuestionContextAsync(question, documentSet);
            if (context == null)
            {
                return $"Document set '{documentSet}' was not found or has no documents.";
            }

            // Get the chat client
            var chatClient = _client.GetChatClient(_options.DeploymentName);

            // Create chat messages
            var systemMessage = new SystemChatMessage(_options.SystemMessage);
            var userMessage = new UserChatMessage(
                $"Here is documentation about Sekiban:\n\n{context}\n\nBased on this information, please answer the following question: {question}");

            var messages = new ChatMessage[] { systemMessage, userMessage };

            // Complete chat
            var response = await chatClient.CompleteChatAsync(messages);

            return ExtractResponseContent(response.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering question");
            return $"I'm sorry, but an error occurred when trying to answer your question: {ex.Message}";
        }
    }

    private async Task<StringBuilder?> BuildQuestionContextAsync(string question, string documentSet)
    {
        var searchResults = await _documentService.SearchAsync(question, documentSet);
        if (searchResults.Count == 0)
        {
            var firstDocument = await _documentService.GetDocumentByIndexAsync(0, documentSet);
            return firstDocument == null ? null : new StringBuilder(firstDocument.Content);
        }

        var context = new StringBuilder();
        foreach (var result in searchResults.Take(3))
        {
            await AppendSearchResultContextAsync(context, result, documentSet);
        }

        return context;
    }

    private async Task AppendSearchResultContextAsync(StringBuilder context, SearchResult result, string documentSet)
    {
        var document = await _documentService.GetDocumentAsync(result.FileName, documentSet);
        if (document == null)
        {
            return;
        }

        context.AppendLine($"# {document.Title}");
        foreach (var section in result.MatchedSections.Take(2))
        {
            var sectionContent = document.GetSectionContent(section);
            context.AppendLine($"## {section}");
            context.AppendLine(sectionContent);
        }
    }

    private string ExtractResponseContent(ChatCompletion response)
    {
        try
        {
            return response.Content.FirstOrDefault()?.Text ?? "No response content";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting content from Azure OpenAI response");
            return "Error processing response from Azure OpenAI";
        }
    }
}
