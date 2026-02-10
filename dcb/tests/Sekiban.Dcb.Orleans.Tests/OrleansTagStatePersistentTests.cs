using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class OrleansTagStatePersistentTests
{
    private readonly ITagProjectionRuntime _tagProjectionRuntime;

    public OrleansTagStatePersistentTests()
    {
        var simpleTypes = new SimpleTagStatePayloadTypes(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        // Register test payload types
        simpleTypes.RegisterPayloadType<TestStatePayload>();
        _tagProjectionRuntime = new TestTagProjectionRuntime(simpleTypes);
    }

    [Fact]
    public async Task SaveAndLoadState_WithSerializableTagState_ShouldSucceed()
    {
        // Arrange
        var testPayload = new TestStatePayload
        {
            Id = Guid.NewGuid(),
            Name = "Test State",
            Value = 100
        };

        var originalState = new TagState(
            testPayload,
            1,
            "unique-id-123",
            "TestGroup",
            "TestContent",
            "TestProjector",
            "1.0.0");

        // Create in-memory cache state for testing
        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);

        var persistent = new OrleansTagStatePersistent(persistentState, _tagProjectionRuntime);

        // Act - Save
        await persistent.SaveStateAsync(originalState);

        // Assert - Saved as SerializableTagState
        Assert.NotNull(persistentState.State.CachedState);
        var serializable = persistentState.State.CachedState;
        Assert.Equal(originalState.Version, serializable.Version);
        Assert.Equal(originalState.TagGroup, serializable.TagGroup);
        Assert.Equal(nameof(TestStatePayload), serializable.TagPayloadName);
        Assert.NotEmpty(serializable.Payload);

        // Act - Load
        var loadedState = await persistent.LoadStateAsync();

        // Assert - Loaded correctly
        Assert.NotNull(loadedState);
        Assert.Equal(originalState.Version, loadedState.Version);
        Assert.Equal(originalState.TagGroup, loadedState.TagGroup);
        Assert.Equal(originalState.TagContent, loadedState.TagContent);

        var loadedPayload = loadedState.Payload as TestStatePayload;
        Assert.NotNull(loadedPayload);
        Assert.Equal(testPayload.Id, loadedPayload.Id);
        Assert.Equal(testPayload.Name, loadedPayload.Name);
        Assert.Equal(testPayload.Value, loadedPayload.Value);
    }

    [Fact]
    public async Task LoadState_WithEmptyCache_ShouldReturnNull()
    {
        // Arrange
        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);
        var persistent = new OrleansTagStatePersistent(persistentState, _tagProjectionRuntime);

        // Act
        var result = await persistent.LoadStateAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ClearState_ShouldRemoveCachedState()
    {
        // Arrange
        var testPayload = new TestStatePayload { Id = Guid.NewGuid(), Name = "Test" };
        var state = new TagState(testPayload, 1, "id", "group", "content", "projector", "1.0");

        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);
        var persistent = new OrleansTagStatePersistent(persistentState, _tagProjectionRuntime);

        await persistent.SaveStateAsync(state);
        Assert.NotNull(persistentState.State.CachedState);

        // Act
        await persistent.ClearStateAsync();

        // Assert
        Assert.Null(persistentState.State.CachedState);
    }

    [Fact]
    public async Task SaveAndLoad_WithEmptyTagStatePayload_ShouldSucceed()
    {
        // Arrange
        var emptyPayload = new EmptyTagStatePayload();
        var state = new TagState(
            emptyPayload,
            0,
            string.Empty,
            "EmptyGroup",
            "EmptyContent",
            "EmptyProjector",
            "1.0.0");

        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);
        var persistent = new OrleansTagStatePersistent(persistentState, _tagProjectionRuntime);

        // Act
        await persistent.SaveStateAsync(state);
        var loaded = await persistent.LoadStateAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.IsType<EmptyTagStatePayload>(loaded.Payload);
        Assert.Equal(state.Version, loaded.Version);
        Assert.Equal(state.TagGroup, loaded.TagGroup);
    }

    // Test helper class
    private class TestPersistentState<T> : IPersistentState<T> where T : new()
    {
        public TestPersistentState(T state)
        {
            State = state;
        }

        public T State { get; set; }
        public string Etag => "test-etag";
        public bool RecordExists => State != null;

        public Task ClearStateAsync()
        {
            State = new T();
            return Task.CompletedTask;
        }

        public Task ReadStateAsync() => Task.CompletedTask;

        public Task WriteStateAsync()
        {
            // Simulate write
            return Task.CompletedTask;
        }
    }

    // Test payload type
    public record TestStatePayload : ITagStatePayload
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Value { get; init; }
    }

    /// <summary>
    ///     Minimal ITagProjectionRuntime for tests — only SerializePayload / DeserializePayload
    ///     are exercised by OrleansTagStatePersistent.
    /// </summary>
    private class TestTagProjectionRuntime : ITagProjectionRuntime
    {
        private readonly ITagStatePayloadTypes _payloadTypes;

        public TestTagProjectionRuntime(ITagStatePayloadTypes payloadTypes) =>
            _payloadTypes = payloadTypes;

        public ResultBox<ITagProjector> GetProjector(string tagProjectorName) =>
            ResultBox.Error<ITagProjector>(new NotSupportedException());

        public ResultBox<string> GetProjectorVersion(string tagProjectorName) =>
            ResultBox.Error<string>(new NotSupportedException());

        public IReadOnlyList<string> GetAllProjectorNames() => Array.Empty<string>();

        public string? TryGetProjectorForTagGroup(string tagGroupName) => null;

        public ITag ResolveTag(string tagString) =>
            throw new NotSupportedException();

        public ResultBox<byte[]> SerializePayload(ITagStatePayload payload) =>
            _payloadTypes.SerializePayload(payload);

        public ResultBox<ITagStatePayload> DeserializePayload(string payloadName, byte[] data) =>
            _payloadTypes.DeserializePayload(payloadName, data);
    }
}