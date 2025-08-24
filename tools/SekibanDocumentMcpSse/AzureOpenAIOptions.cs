namespace SekibanDocumentMcpSse;

/// <summary>
///     Options for Azure OpenAI service
/// </summary>
public class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>
    ///     Azure OpenAI endpoint
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    ///     Azure OpenAI API key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    ///     Deployment name for the model
    /// </summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>
    ///     Embedding model deployment name
    /// </summary>
    public string EmbeddingDeploymentName { get; set; } = string.Empty;

    /// <summary>
    ///     System message for chat completion
    /// </summary>
    public string SystemMessage { get; set; }
        = "You are a helpful assistant that answers questions about Sekiban Event Sourcing framework based on the provided documentation.";

    /// <summary>
    ///     Maximum tokens for the response
    /// </summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    ///     Temperature for sampling (0.0 - 1.0)
    /// </summary>
    public float Temperature { get; set; } = 0.7f;
}
