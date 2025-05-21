using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using System.Text;

namespace SekibanDocumentMcpSse;

/// <summary>
/// Service for interacting with Azure OpenAI
/// </summary>
public class AzureOpenAIService
{
    private readonly ILogger<AzureOpenAIService> _logger;
    private readonly AzureOpenAIOptions _options;
    private readonly SekibanDocumentService _documentService;
    private OpenAIClient? _client;

    /// <summary>
    /// Constructor
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
                _client = new OpenAIClient(
                    new Uri(_options.Endpoint),
                    new AzureKeyCredential(_options.ApiKey));
                _logger.LogInformation("Azure OpenAI client initialized");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure OpenAI client");
            }
        }
        else
        {
            _logger.LogWarning("Azure OpenAI settings are missing. Question answering will not be available.");
        }
    }

    /// <summary>
    /// Answer a question about Sekiban using Azure OpenAI
    /// </summary>
    public async Task<string> AnswerQuestionAsync(string question)
    {
        if (_client == null || string.IsNullOrEmpty(_options.DeploymentName))
        {
            return "I'm sorry, but Azure OpenAI is not configured properly. Please add valid Azure OpenAI settings to appsettings.json.";
        }
        
        try
        {
            // Search for relevant documents
            var searchResults = await _documentService.SearchAsync(question);
            
            // Prepare context from search results
            var context = new StringBuilder();
            foreach (var result in searchResults.Take(3))
            {
                var document = await _documentService.GetDocumentAsync(result.FileName);
                if (document != null)
                {
                    context.AppendLine($"# {document.Title}");
                    foreach (var section in result.MatchedSections.Take(2))
                    {
                        var sectionContent = document.GetSectionContent(section);
                        context.AppendLine($"## {section}");
                        context.AppendLine(sectionContent);
                    }
                }
            }
            
            // If no search results, include basic information
            if (searchResults.Count == 0)
            {
                var firstDocument = await _documentService.GetDocumentByIndexAsync(0);
                if (firstDocument != null)
                {
                    context.AppendLine(firstDocument.Content);
                }
            }
            
            // Create chat completion options
            var chatCompletionOptions = new ChatCompletionsOptions
            {
                DeploymentName = _options.DeploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage(_options.SystemMessage),
                    new ChatRequestUserMessage($"Here is documentation about Sekiban:\n\n{context}\n\nBased on this information, please answer the following question: {question}")
                },
                MaxTokens = _options.MaxTokens,
                Temperature = _options.Temperature
            };
            
            // Get the response from Azure OpenAI
            var response = await _client.GetChatCompletionsAsync(chatCompletionOptions);
            var responseMessage = response.Value.Choices[0].Message;
            
            return responseMessage.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering question");
            return $"I'm sorry, but an error occurred when trying to answer your question: {ex.Message}";
        }
    }
}