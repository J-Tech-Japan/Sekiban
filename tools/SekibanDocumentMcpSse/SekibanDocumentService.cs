using Microsoft.Extensions.Options;
namespace SekibanDocumentMcpSse;

/// <summary>
///     Service for handling Sekiban documentation
/// </summary>
public class SekibanDocumentService : IDisposable
{
    private static readonly Dictionary<string, string[]> DocumentSetPrefixes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dcb"] = ["dcb_llm", "dcb_llm_ja"],
            ["Pure"] = ["llm", "llm_ja"]
        };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SekibanDocumentService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<MarkdownReader> _markdownReaders = [];
    private readonly DocumentationOptions _options;
    private List<MarkdownDocument> _documents = new();
    private readonly List<FileSystemWatcher> _fileWatchers = [];
    private bool _isInitialized;

    /// <summary>
    ///     Constructor
    /// </summary>
    public SekibanDocumentService(
        ILogger<SekibanDocumentService> logger,
        ILoggerFactory loggerFactory,
        IOptions<DocumentationOptions> options,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _options = options.Value;
        _environment = environment;

        foreach (var configuredPath in GetConfiguredBasePaths(_options))
        {
            var docsBasePath = ResolveDocsBasePath(configuredPath);
            var documentSet = GetDocumentSetName(configuredPath);
            logger.LogInformation(
                "Registering documentation path {DocsBasePath} as document set {DocumentSet}",
                docsBasePath,
                documentSet);
            _markdownReaders.Add(
                new MarkdownReader(_loggerFactory.CreateLogger<MarkdownReader>(), docsBasePath, documentSet));
        }
    }

    /// <summary>
    ///     Dispose resources
    /// </summary>
    public void Dispose()
    {
        foreach (var fileWatcher in _fileWatchers)
        {
            fileWatcher.Changed -= OnFileChanged;
            fileWatcher.Created -= OnFileChanged;
            fileWatcher.Deleted -= OnFileChanged;
            fileWatcher.Renamed -= OnFileChanged;
            fileWatcher.Dispose();
        }

        _fileWatchers.Clear();
    }

    /// <summary>
    ///     Initialize the service and load all documents
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            var documents = new List<MarkdownDocument>();
            foreach (var reader in _markdownReaders)
            {
                documents.AddRange(await reader.ReadAllDocumentsAsync());
            }

            _documents = documents.OrderBy(d => d.FileName, StringComparer.OrdinalIgnoreCase).ToList();
            _logger.LogInformation("Loaded {Count} Markdown documents", _documents.Count);

            if (_options.EnableFileWatcher)
            {
                SetupFileWatchers();
            }

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize documentation service");
            throw;
        }
    }

    /// <summary>
    ///     Setup file watcher to reload documents when they change
    /// </summary>
    private void SetupFileWatchers()
    {
        foreach (var directory in _markdownReaders.Select(reader => reader._docsBasePath))
        {
            try
            {
                var fileWatcher = new FileSystemWatcher(directory)
                {
                    Filter = "*.md",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };

                fileWatcher.Changed += OnFileChanged;
                fileWatcher.Created += OnFileChanged;
                fileWatcher.Deleted += OnFileChanged;
                fileWatcher.Renamed += OnFileChanged;
                _fileWatchers.Add(fileWatcher);

                _logger.LogInformation("File watcher set up for directory: {Directory}", directory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set up file watcher");
            }
        }
    }

    /// <summary>
    ///     Handle file changes
    /// </summary>
    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            _logger.LogInformation("Document file changed: {FullPath}, reloading documents", e.FullPath);
            await Task.Delay(500); // Small delay to ensure file is fully written
            var documents = new List<MarkdownDocument>();
            foreach (var reader in _markdownReaders)
            {
                documents.AddRange(await reader.ReadAllDocumentsAsync());
            }

            _documents = documents.OrderBy(d => d.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file change event");
        }
    }

    /// <summary>
    ///     Get all document titles
    /// </summary>
    public async Task<List<DocumentInfo>> GetAllDocumentsAsync(string documentSet)
    {
        await InitializeAsync();
        return GetDocumentsForSet(documentSet)
            .Select(d => new DocumentInfo
            {
                FileName = d.FileName,
                Title = d.Title,
                Sections = d.Sections
            })
            .ToList();
    }

    /// <summary>
    ///     Get a document by filename
    /// </summary>
    public async Task<MarkdownDocument?> GetDocumentAsync(string fileName, string documentSet)
    {
        await InitializeAsync();
        var normalizedFileName = NormalizeFileNameForSet(fileName, documentSet);
        if (normalizedFileName == null)
        {
            return null;
        }

        return GetDocumentsForSet(documentSet)
            .FirstOrDefault(d => d.FileName.Equals(normalizedFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Get a document by index
    /// </summary>
    public async Task<MarkdownDocument?> GetDocumentByIndexAsync(int index, string documentSet)
    {
        await InitializeAsync();
        var documents = GetDocumentsForSet(documentSet).ToList();
        if (index >= 0 && index < documents.Count)
        {
            return documents[index];
        }
        return null;
    }

    /// <summary>
    ///     Get the navigation structure
    /// </summary>
    public async Task<List<NavigationItem>> GetNavigationAsync(string documentSet)
    {
        await InitializeAsync();
        var navigation = new List<NavigationItem>();

        foreach (var doc in GetDocumentsForSet(documentSet))
        {
            navigation.Add(
                new NavigationItem
                {
                    Title = doc.Title,
                    FileName = doc.FileName,
                    Sections = doc
                        .Sections
                        .Select(s => new NavigationSection
                        {
                            Title = s
                        })
                        .ToList()
                });
        }

        return navigation;
    }

    /// <summary>
    ///     Get a specific section from a document
    /// </summary>
    public async Task<SectionContent?> GetSectionContentAsync(string fileName, string sectionTitle, string documentSet)
    {
        await InitializeAsync();
        var document = await GetDocumentAsync(fileName, documentSet);
        if (document == null) return null;

        var content = document.GetSectionContent(sectionTitle);
        if (string.IsNullOrEmpty(content)) return null;

        return new SectionContent
        {
            DocumentTitle = document.Title,
            SectionTitle = sectionTitle,
            Content = content
        };
    }

    /// <summary>
    ///     Search across all documents
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query, string documentSet)
    {
        await InitializeAsync();
        var results = new List<SearchResult>();
        var searchTerms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var document in GetDocumentsForSet(documentSet))
        {
            // Search in title
            var titleMatched = searchTerms.All(term => document.Title.ToLower().Contains(term));

            // Search in content
            var contentMatches = new List<string>();
            foreach (var section in document.Sections)
            {
                var sectionContent = document.GetSectionContent(section);
                if (searchTerms.All(term => sectionContent.ToLower().Contains(term)))
                {
                    contentMatches.Add(section);
                }
            }

            if (titleMatched || contentMatches.Count > 0)
            {
                results.Add(
                    new SearchResult
                    {
                        DocumentTitle = document.Title,
                        FileName = document.FileName,
                        MatchedInTitle = titleMatched,
                        MatchedSections = contentMatches
                    });
            }
        }

        return results;
    }

    /// <summary>
    ///     Get all code samples across documents
    /// </summary>
    public async Task<List<SekibanCodeSample>> GetAllCodeSamplesAsync()
    {
        await InitializeAsync();
        var samples = new List<SekibanCodeSample>();

        foreach (var document in _documents)
        {
            foreach (var sample in document.CodeSamples)
            {
                samples.Add(
                    new SekibanCodeSample
                    {
                        Title = sample.Context,
                        Language = sample.Language,
                        Code = sample.Code,
                        DocumentTitle = document.Title,
                        FileName = document.FileName
                    });
            }
        }

        return samples;
    }

    /// <summary>
    ///     Get code samples by language
    /// </summary>
    public async Task<List<SekibanCodeSample>> GetCodeSamplesByLanguageAsync(string language)
    {
        var allSamples = await GetAllCodeSamplesAsync();
        return allSamples.Where(s => s.Language.Equals(language, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    ///     Search for code samples
    /// </summary>
    public async Task<List<SekibanCodeSample>> SearchCodeSamplesAsync(string query)
    {
        var allSamples = await GetAllCodeSamplesAsync();
        var searchTerms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return allSamples
            .Where(s => searchTerms.All(term => s.Title.ToLower().Contains(term) || s.Code.ToLower().Contains(term)))
            .ToList();
    }

    private static IReadOnlyList<string> GetConfiguredBasePaths(DocumentationOptions options) =>
        options.BasePaths.Count > 0 ? options.BasePaths : [options.BasePath];

    private IEnumerable<MarkdownDocument> GetDocumentsForSet(string documentSet)
    {
        var prefixes = GetDocumentSetPrefixes(documentSet);
        if (prefixes.Count == 0)
        {
            return [];
        }

        return _documents.Where(
            d => prefixes.Any(prefix => d.FileName.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? NormalizeFileNameForSet(string fileName, string documentSet)
    {
        var prefixes = GetDocumentSetPrefixes(documentSet);
        if (prefixes.Count == 0 || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var normalizedFileName = fileName.Replace('\\', '/').TrimStart('/');
        if (normalizedFileName.Contains('/'))
        {
            return prefixes.Any(prefix => normalizedFileName.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase))
                ? normalizedFileName
                : null;
        }

        return prefixes
            .Select(prefix => $"{prefix}/{Path.GetFileName(normalizedFileName)}")
            .FirstOrDefault();
    }

    private static IReadOnlyList<string> GetDocumentSetPrefixes(string documentSet)
    {
        var normalized = documentSet?.Trim() ?? string.Empty;
        return DocumentSetPrefixes.TryGetValue(normalized, out var prefixes) ? prefixes : [];
    }

    private static string ResolveDocsBasePath(string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

    private static string GetDocumentSetName(string configuredPath)
    {
        var normalized = configuredPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return Path.GetFileName(normalized);
    }
}
