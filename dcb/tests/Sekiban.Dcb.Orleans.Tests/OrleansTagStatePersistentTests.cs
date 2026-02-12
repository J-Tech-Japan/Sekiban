using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class OrleansTagStatePersistentTests
{
    private readonly ITagStatePayloadTypes _tagStatePayloadTypes;

    public OrleansTagStatePersistentTests()
    {
        var simpleTypes = new SimpleTagStatePayloadTypes(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        // Register test payload types
        simpleTypes.RegisterPayloadType<TestStatePayload>();
        _tagStatePayloadTypes = simpleTypes;
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

        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

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
        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

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
        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

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
        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

        // Act
        await persistent.SaveStateAsync(state);
        var loaded = await persistent.LoadStateAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.IsType<EmptyTagStatePayload>(loaded.Payload);
        Assert.Equal(state.Version, loaded.Version);
        Assert.Equal(state.TagGroup, loaded.TagGroup);
    }

    [Fact]
    public async Task SaveAndLoadSerializableState_DirectPath_ShouldSucceed()
    {
        // Arrange
        var serializable = new SerializableTagState(
            Payload: System.Text.Encoding.UTF8.GetBytes("{\"count\":1}"),
            Version: 3,
            LastSortedUniqueId: "uid-003",
            TagGroup: "Group",
            TagContent: "Content",
            TagProjector: "Projector",
            TagPayloadName: "WasmJsonTagState",
            ProjectorVersion: "v1");

        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);
        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

        // Act
        await persistent.SaveSerializableStateAsync(serializable);
        var loaded = await persistent.LoadSerializableStateAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(serializable.Version, loaded.Version);
        Assert.Equal(serializable.LastSortedUniqueId, loaded.LastSortedUniqueId);
        Assert.Equal(serializable.TagPayloadName, loaded.TagPayloadName);
        Assert.Equal(serializable.Payload, loaded.Payload);
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

}
