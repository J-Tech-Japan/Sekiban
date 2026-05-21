namespace SekibanDocumentMcpSse;

/// <summary>
///     Options for documentation service
/// </summary>
public class DocumentationOptions
{
    public const string SectionName = "Documentation";

    /// <summary>
    ///     Legacy single base path for documents.
    /// </summary>
    public string BasePath { get; set; } = "docs/dcb_llm";

    /// <summary>
    ///     Base paths for document sets. Each path is exposed under its directory name
    ///     (for example, docs/dcb_llm/01_core_concepts.md becomes dcb_llm/01_core_concepts.md).
    /// </summary>
    public List<string> BasePaths { get; set; } = ["docs/dcb_llm", "docs/dcb_llm_ja", "docs/llm", "docs/llm_ja"];

    /// <summary>
    ///     Enable file watcher to automatically reload docs when changed
    /// </summary>
    public bool EnableFileWatcher { get; set; } = true;
}
