using Microsoft.Extensions.Options;

namespace SekibanDocumentMcpSse;

/// <summary>
/// Service for handling Sekiban documentation
/// </summary>
public class SekibanDocumentService : IDisposable
{
    private readonly ILogger<SekibanDocumentService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MarkdownReader _markdownReader;
    private readonly DocumentationOptions _options;
    private readonly IWebHostEnvironment _environment;
    private List<MarkdownDocument> _documents = new();
    private bool _isInitialized;
    private FileSystemWatcher? _fileWatcher;

    /// <summary>
    /// Constructor
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
        
        // Resolve base path - use absolute path if provided, otherwise combine with content root
        string docsBasePath = _options.BasePath;
        if (!Path.IsPathRooted(docsBasePath))
        {
            // For Azure deployment, use WebRootPath first, then fall back to ContentRootPath
            string basePath = AppContext.BaseDirectory;
            logger.LogInformation("AppContext.BaseDirectory: {BasePath}", basePath);
            docsBasePath = Path.Combine(basePath, docsBasePath);
        }
        
        _markdownReader = new MarkdownReader(
            _loggerFactory.CreateLogger<MarkdownReader>(),
            docsBasePath);
    }

    /// <summary>
    /// Initialize the service and load all documents
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            _documents = await _markdownReader.ReadAllDocumentsAsync();
            _logger.LogInformation("Loaded {Count} Markdown documents", _documents.Count);
            
            if (_options.EnableFileWatcher)
            {
                SetupFileWatcher();
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
    /// Setup file watcher to reload documents when they change
    /// </summary>
    private void SetupFileWatcher()
    {
        try
        {
            string directory = Path.GetDirectoryName(_markdownReader._docsBasePath) ?? _markdownReader._docsBasePath;
            
            _fileWatcher = new FileSystemWatcher(directory)
            {
                Filter = "*.md",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            
            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Created += OnFileChanged;
            _fileWatcher.Deleted += OnFileChanged;
            _fileWatcher.Renamed += OnFileChanged;
            
            _logger.LogInformation("File watcher set up for directory: {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set up file watcher");
        }
    }

    /// <summary>
    /// Handle file changes
    /// </summary>
    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            _logger.LogInformation("Document file changed: {FullPath}, reloading documents", e.FullPath);
            await Task.Delay(500); // Small delay to ensure file is fully written
            _documents = await _markdownReader.ReadAllDocumentsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file change event");
        }
    }
    
    /// <summary>
    /// Get all document titles
    /// </summary>
    public async Task<List<DocumentInfo>> GetAllDocumentsAsync()
    {
        await InitializeAsync();
        return _documents.Select(d => new DocumentInfo
        {
            FileName = d.FileName,
            Title = d.Title,
            Sections = d.Sections
        }).ToList();
    }
    
    /// <summary>
    /// Get a document by filename
    /// </summary>
    public async Task<MarkdownDocument?> GetDocumentAsync(string fileName)
    {
        await InitializeAsync();
        return _documents.FirstOrDefault(d => d.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Get a document by index
    /// </summary>
    public async Task<MarkdownDocument?> GetDocumentByIndexAsync(int index)
    {
        await InitializeAsync();
        if (index >= 0 && index < _documents.Count)
        {
            return _documents[index];
        }
        return null;
    }
    
    /// <summary>
    /// Get the navigation structure
    /// </summary>
    public async Task<List<NavigationItem>> GetNavigationAsync()
    {
        await InitializeAsync();
        var navigation = new List<NavigationItem>();
        
        foreach (var doc in _documents)
        {
            navigation.Add(new NavigationItem
            {
                Title = doc.Title,
                FileName = doc.FileName,
                Sections = doc.Sections.Select(s => new NavigationSection
                {
                    Title = s
                }).ToList()
            });
        }
        
        return navigation;
    }

    /// <summary>
    /// Get a specific section from a document
    /// </summary>
    public async Task<SectionContent?> GetSectionContentAsync(string fileName, string sectionTitle)
    {
        await InitializeAsync();
        var document = _documents.FirstOrDefault(d => d.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
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
    /// Search across all documents
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query)
    {
        await InitializeAsync();
        var results = new List<SearchResult>();
        var searchTerms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var document in _documents)
        {
            // Search in title
            bool titleMatched = searchTerms.All(term => document.Title.ToLower().Contains(term));
            
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
                results.Add(new SearchResult
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
    /// Get all code samples across documents
    /// </summary>
    public async Task<List<SekibanCodeSample>> GetAllCodeSamplesAsync()
    {
        await InitializeAsync();
        var samples = new List<SekibanCodeSample>();
        
        foreach (var document in _documents)
        {
            foreach (var sample in document.CodeSamples)
            {
                samples.Add(new SekibanCodeSample
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
    /// Get code samples by language
    /// </summary>
    public async Task<List<SekibanCodeSample>> GetCodeSamplesByLanguageAsync(string language)
    {
        var allSamples = await GetAllCodeSamplesAsync();
        return allSamples
            .Where(s => s.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
    
    /// <summary>
    /// Search for code samples
    /// </summary>
    public async Task<List<SekibanCodeSample>> SearchCodeSamplesAsync(string query)
    {
        var allSamples = await GetAllCodeSamplesAsync();
        var searchTerms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        return allSamples
            .Where(s => searchTerms.All(term => 
                s.Title.ToLower().Contains(term) || 
                s.Code.ToLower().Contains(term)))
            .ToList();
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Created -= OnFileChanged;
            _fileWatcher.Deleted -= OnFileChanged;
            _fileWatcher.Renamed -= OnFileChanged;
            _fileWatcher.Dispose();
        }
    }
}

#region Models
#endregion