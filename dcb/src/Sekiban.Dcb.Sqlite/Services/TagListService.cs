using System.Text.Json;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Storage;
using CoreTagListService = Sekiban.Dcb.Services.TagListService;

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
///     Service for listing and exporting tags from the event store. SQLite's historical service surface; the
///     behaviour belongs to the core <see cref="Sekiban.Dcb.Services.TagListService" />.
/// </summary>
public class TagListService
{
    private readonly CoreTagListService _inner;

    public TagListService(IEventStore eventStore, ITagTypes tagTypes, JsonSerializerOptions jsonSerializerOptions)
    {
        _inner = new CoreTagListService(eventStore, tagTypes, jsonSerializerOptions);
    }

    /// <inheritdoc cref="CoreTagListService.GetAllTagsAsync" />
    public Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);

    /// <inheritdoc cref="CoreTagListService.GetTagsByGroupAsync" />
    public async Task<IReadOnlyList<TagGroupSummary>> GetTagsByGroupAsync(string? tagGroup = null) =>
        (await _inner.GetTagsByGroupAsync(tagGroup)).Select(ToSqlite).ToList();

    /// <inheritdoc cref="CoreTagListService.ExportTagListAsync" />
    public async Task<TagListExportResult> ExportTagListAsync(string? outputPath = null, string? tagGroup = null)
    {
        var result = await _inner.ExportTagListAsync(outputPath, tagGroup);
        return new TagListExportResult(
            result.TotalTags,
            result.TotalTagGroups,
            result.TotalEvents,
            result.OutputFilePath,
            result.TagGroups.Select(ToSqlite).ToList());
    }

    /// <inheritdoc cref="CoreTagListService.GetRegisteredTagGroupNames" />
    public IReadOnlyList<string> GetRegisteredTagGroupNames() => _inner.GetRegisteredTagGroupNames();

    /// <inheritdoc cref="CoreTagListService.JsonSerializerOptions" />
    public JsonSerializerOptions JsonSerializerOptions => _inner.JsonSerializerOptions;

    private static TagGroupSummary ToSqlite(Sekiban.Dcb.Services.TagGroupSummary summary) =>
        new(summary.TagGroup, summary.TagCount, summary.TotalEvents, summary.Tags);
}
