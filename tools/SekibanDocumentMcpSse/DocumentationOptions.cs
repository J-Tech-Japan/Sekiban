namespace SekibanDocumentMcpSse;

/// <summary>
///     Options for documentation service
/// </summary>
public class DocumentationOptions
{
    public const string SectionName = "Documentation";

    /// <summary>
    ///     Base path for documents
    /// </summary>
    public string BasePath { get; set; } = "./docs/llm";

    /// <summary>
    ///     Enable file watcher to automatically reload docs when changed
    /// </summary>
    public bool EnableFileWatcher { get; set; } = true;
}
