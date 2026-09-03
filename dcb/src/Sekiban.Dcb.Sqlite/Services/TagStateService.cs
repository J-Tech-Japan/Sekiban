using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using CoreTagStateProjectionResult = Sekiban.Dcb.Services.TagStateProjectionResult;
using CoreTagStateService = Sekiban.Dcb.Services.TagStateService;

namespace Sekiban.Dcb.Sqlite.Services;

/// <summary>
///     Result of projecting a tag state through the SQLite compatibility service.
/// </summary>
public record TagStateProjectionResult(
    ITag Tag,
    string ProjectorName,
    string ProjectorVersion,
    ITagStatePayload State,
    int EventCount,
    string? LastSortableUniqueId);

/// <summary>
///     SQLite's historical service surface. Projection behavior belongs to the core service so native tagged streams
///     follow one consumer policy regardless of the provider registration that reached this compatibility type.
/// </summary>
public class TagStateService
{
    private readonly CoreTagStateService _inner;

    public TagStateService(
        IEventStore eventStore,
        IEventTypes eventTypes,
        ITagTypes tagTypes,
        ITagProjectorTypes tagProjectorTypes,
        JsonSerializerOptions jsonSerializerOptions)
    {
        _inner = new CoreTagStateService(
            eventStore,
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            jsonSerializerOptions);
    }

    /// <inheritdoc cref="CoreTagStateService.ParseTag" />
    public ITag ParseTag(string tagString) => _inner.ParseTag(tagString);

    /// <inheritdoc cref="CoreTagStateService.GetLatestTagStateAsync" />
    public Task<ResultBox<TagState>> GetLatestTagStateAsync(ITag tag) => _inner.GetLatestTagStateAsync(tag);

    /// <inheritdoc cref="CoreTagStateService.GetLatestTagStateByStringAsync" />
    public Task<ResultBox<TagState>> GetLatestTagStateByStringAsync(string tagString) =>
        _inner.GetLatestTagStateByStringAsync(tagString);

    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(string tagString, string projectorName) =>
        ConvertProjectionAsync(_inner.ProjectTagStateAsync(tagString, projectorName));

    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(
        string tagString,
        string projectorName,
        CancellationToken cancellationToken) =>
        ConvertProjectionAsync(_inner.ProjectTagStateAsync(tagString, projectorName, cancellationToken));

    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(string tagString) =>
        ConvertProjectionAsync(_inner.ProjectTagStateAsync(tagString));

    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(
        string tagString,
        CancellationToken cancellationToken) =>
        ConvertProjectionAsync(_inner.ProjectTagStateAsync(tagString, cancellationToken));

    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(ITag tag) =>
        ConvertProjectionAsync(_inner.ProjectTagStateAsync(tag));

    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(ITag tag, CancellationToken cancellationToken) =>
        ConvertProjectionAsync(_inner.ProjectTagStateAsync(tag, cancellationToken));

    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(ITag tag, string projectorName) =>
        ConvertProjectionAsync(_inner.ProjectTagStateAsync(tag, projectorName));

    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(
        ITag tag,
        string projectorName,
        CancellationToken cancellationToken) =>
        ConvertProjectionAsync(_inner.ProjectTagStateAsync(tag, projectorName, cancellationToken));

    /// <inheritdoc cref="CoreTagStateService.GetAllTagProjectorNames" />
    public IReadOnlyList<string> GetAllTagProjectorNames() => _inner.GetAllTagProjectorNames();

    /// <inheritdoc cref="CoreTagStateService.GetAllTagGroupNames" />
    public IReadOnlyList<string> GetAllTagGroupNames() => _inner.GetAllTagGroupNames();

    /// <inheritdoc cref="CoreTagStateService.JsonSerializerOptions" />
    public JsonSerializerOptions JsonSerializerOptions => _inner.JsonSerializerOptions;

    private static async Task<ResultBox<TagStateProjectionResult>> ConvertProjectionAsync(
        Task<ResultBox<CoreTagStateProjectionResult>> projectionTask)
    {
        var projectionResult = await projectionTask;
        if (!projectionResult.IsSuccess)
        {
            return ResultBox.Error<TagStateProjectionResult>(projectionResult.GetException());
        }

        var projection = projectionResult.GetValue();
        return ResultBox.FromValue(
            new TagStateProjectionResult(
                projection.Tag,
                projection.ProjectorName,
                projection.ProjectorVersion,
                projection.State,
                projection.EventCount,
                projection.LastSortableUniqueId));
    }
}
