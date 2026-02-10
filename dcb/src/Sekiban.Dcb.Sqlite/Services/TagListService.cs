using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Sqlite.Services;

/// <summary>
///     Result of exporting a tag list
/// </summary>
public record TagListExportResult(
    int TotalTags,
    int TotalTagGroups,
    long TotalEvents,
    string? OutputFilePath,
    IReadOnlyList<TagGroupSummary> TagGroups);

/// <summary>
///     Summary of a tag group
/// </summary>
public record TagGroupSummary(
    string TagGroup,
    int TagCount,
    long TotalEvents,
    IReadOnlyList<TagInfo> Tags);

/// <summary>
///     Service for listing and exporting tags from the event store
/// </summary>
public class TagListService
{
    private readonly IEventStore _eventStore;
    private readonly ITagTypes _tagTypes;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public TagListService(IEventStore eventStore, ITagTypes tagTypes, JsonSerializerOptions jsonSerializerOptions)
    {
        _eventStore = eventStore;
        _tagTypes = tagTypes;
        _jsonSerializerOptions = jsonSerializerOptions;
    }

    /// <summary>
    ///     Get all tags from the event store
    /// </summary>
    /// <param name="tagGroup">Optional: Filter by tag group name</param>
    /// <returns>List of tag information</returns>
    public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
        => _eventStore.GetAllTagsAsync(tagGroup);

    /// <summary>
    ///     Get all tags grouped by tag group
    /// </summary>
    /// <param name="tagGroup">Optional: Filter by tag group name</param>
    /// <returns>List of tag group summaries</returns>
    public async Task<ResultBox<IReadOnlyList<TagGroupSummary>>> GetTagsByGroupAsync(string? tagGroup = null)
    {
        var tagsResult = await _eventStore.GetAllTagsAsync(tagGroup);
        if (!tagsResult.IsSuccess)
        {
            return ResultBox.Error<IReadOnlyList<TagGroupSummary>>(tagsResult.GetException());
        }

        var tags = tagsResult.GetValue().ToList();
        var grouped = tags
            .GroupBy(t => t.TagGroup)
            .Select(g => new TagGroupSummary(
                g.Key,
                g.Count(),
                g.Sum(t => t.EventCount),
                g.OrderBy(t => t.Tag).ToList()))
            .OrderBy(g => g.TagGroup)
            .ToList();

        return ResultBox.FromValue<IReadOnlyList<TagGroupSummary>>(grouped);
    }

    /// <summary>
    ///     Export tag list to a JSON file
    /// </summary>
    /// <param name="outputPath">Output file path (if null, returns result without saving)</param>
    /// <param name="tagGroup">Optional: Filter by tag group name</param>
    /// <returns>Export result with tag information</returns>
    public async Task<ResultBox<TagListExportResult>> ExportTagListAsync(string? outputPath = null, string? tagGroup = null)
    {
        var groupsResult = await GetTagsByGroupAsync(tagGroup);
        if (!groupsResult.IsSuccess)
        {
            return ResultBox.Error<TagListExportResult>(groupsResult.GetException());
        }

        var groups = groupsResult.GetValue();
        var totalTags = groups.Sum(g => g.TagCount);
        var totalEvents = groups.Sum(g => g.TotalEvents);

        var result = new TagListExportResult(
            totalTags,
            groups.Count,
            totalEvents,
            outputPath,
            groups);

        if (!string.IsNullOrEmpty(outputPath))
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var exportData = new
            {
                ExportedAt = DateTime.UtcNow,
                TotalTags = totalTags,
                TotalTagGroups = groups.Count,
                TotalEvents = totalEvents,
                TagGroups = groups.Select(g => new
                {
                    g.TagGroup,
                    g.TagCount,
                    g.TotalEvents,
                    Tags = g.Tags.Select(t => new
                    {
                        t.Tag,
                        t.EventCount,
                        t.FirstSortableUniqueId,
                        t.LastSortableUniqueId,
                        t.FirstEventAt,
                        t.LastEventAt
                    })
                })
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(exportData, jsonOptions);
            await File.WriteAllTextAsync(outputPath, json);
        }

        return ResultBox.FromValue(result);
    }

    /// <summary>
    ///     Get all registered tag group names from domain types
    /// </summary>
    public IReadOnlyList<string> GetRegisteredTagGroupNames()
        => _tagTypes.GetAllTagGroupNames();

    /// <summary>
    ///     Get the JSON serializer options from domain types
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions => _jsonSerializerOptions;
}
