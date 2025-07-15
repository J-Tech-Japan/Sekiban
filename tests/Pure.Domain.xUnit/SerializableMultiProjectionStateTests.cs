using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Projectors;
using Pure.Domain;
using Pure.Domain.Generated;
using ResultBoxes;
using Sekiban.Pure;

namespace Pure.Domain.xUnit;

/// <summary>
/// Test class for testing serialization/deserialization of SerializableMultiProjectionState
/// </summary>
public class SerializableMultiProjectionStateTests
{
    private readonly SekibanDomainTypes _domainTypes;

    public SerializableMultiProjectionStateTests()
    {
        _domainTypes = PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options);
    }

    #region Helper Methods

    /// <summary>
    /// Creates a MultiProjectorPayload for testing
    /// </summary>
    private MultiProjectorPayload CreateTestMultiProjectorPayload()
    {
        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();
        var users = ImmutableDictionary<Guid, MultiProjectorPayload.User>.Empty
            .Add(user1Id, new MultiProjectorPayload.User(user1Id, "User1", "user1@example.com", true))
            .Add(user2Id, new MultiProjectorPayload.User(user2Id, "User2", "user2@example.com", false));

        var cart1Id = Guid.NewGuid();
        var cart2Id = Guid.NewGuid();
        var cart1Items = ImmutableList<MultiProjectorPayload.Item>.Empty
            .Add(new MultiProjectorPayload.Item(Guid.NewGuid(), "Item1", 2, 100.0m))
            .Add(new MultiProjectorPayload.Item(Guid.NewGuid(), "Item2", 1, 50.0m));
        var cart2Items = ImmutableList<MultiProjectorPayload.Item>.Empty
            .Add(new MultiProjectorPayload.Item(Guid.NewGuid(), "Item3", 3, 30.0m));

        var carts = ImmutableDictionary<Guid, MultiProjectorPayload.Cart>.Empty
            .Add(cart1Id, new MultiProjectorPayload.Cart(cart1Id, user1Id, cart1Items, "CreditCard"))
            .Add(cart2Id, new MultiProjectorPayload.Cart(cart2Id, user2Id, cart2Items, "PayPal"));

        return new MultiProjectorPayload(users, carts);
    }

    /// <summary>
    /// Creates an AggregateListProjector&lt;BranchProjector&gt; for testing
    /// </summary>
    private AggregateListProjector<BranchProjector> CreateTestAggregateListProjector()
    {
        var branch1Id = Guid.NewGuid();
        var branch2Id = Guid.NewGuid();
        
        var partitionKeys1 = new PartitionKeys(branch1Id, typeof(BranchProjector).Name, "test");
        var partitionKeys2 = new PartitionKeys(branch2Id, typeof(BranchProjector).Name, "test");
        
        var branch1 = new Branch("Tokyo Branch");
        var branch2 = new Branch("Osaka Branch");
        
        var aggregate1 = new Aggregate(
            branch1, 
            partitionKeys1, 
            1, 
            "testSortable1", 
            "1.0.0", 
            typeof(BranchProjector).Name,
            typeof(Branch).Name);
        var aggregate2 = new Aggregate(
            branch2, 
            partitionKeys2, 
            2, 
            "testSortable2", 
            "1.0.0", 
            typeof(BranchProjector).Name,
            typeof(Branch).Name);
        
        var aggregates = ImmutableDictionary<PartitionKeys, Aggregate>.Empty
            .Add(partitionKeys1, aggregate1)
            .Add(partitionKeys2, aggregate2);
        
        return new AggregateListProjector<BranchProjector>(aggregates);
    }

    /// <summary>
    /// Creates a MultiProjectionState
    /// </summary>
    private MultiProjectionState CreateMultiProjectionState<TProjection>(TProjection projector)
        where TProjection : IMultiProjectorCommon
    {
        return new MultiProjectionState(
            projector,
            Guid.NewGuid(),
            "20250331T123456Z",
            42,
            21,
            "test-root-partition-key");
    }

    /// <summary>
    /// Compares the contents of MultiProjectionState
    /// </summary>
    private void AssertMultiProjectionStatesEqual(
        MultiProjectionState expected,
        MultiProjectionState actual)
    {
        Assert.Equal(expected.LastEventId, actual.LastEventId);
        Assert.Equal(expected.LastSortableUniqueId, actual.LastSortableUniqueId);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.AppliedSnapshotVersion, actual.AppliedSnapshotVersion);
        Assert.Equal(expected.RootPartitionKey, actual.RootPartitionKey);
        
        Assert.Equal(
            expected.ProjectorCommon.GetType().FullName,
            actual.ProjectorCommon.GetType().FullName);
    }

    /// <summary>
    /// Custom converter for JSON conversion of PartitionKeys
    /// This is necessary when using PartitionKeys as dictionary keys
    /// </summary>
    private class PartitionKeysJsonConverter : JsonConverter<PartitionKeys>
    {
        public override PartitionKeys Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token");
            }

            Guid aggregateId = default;
            string group = string.Empty;
            string rootPartitionKey = string.Empty;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new PartitionKeys(aggregateId, group, rootPartitionKey);
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token");
                }

                var propertyName = reader.GetString();
                reader.Read();

                switch (propertyName)
                {
                    case "aggregateId":
                        aggregateId = reader.GetGuid();
                        break;
                    case "group":
                        group = reader.GetString() ?? string.Empty;
                        break;
                    case "rootPartitionKey":
                        rootPartitionKey = reader.GetString() ?? string.Empty;
                        break;
                }
            }

            throw new JsonException("Expected EndObject token");
        }

        public override void Write(Utf8JsonWriter writer, PartitionKeys value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("aggregateId");
            writer.WriteStringValue(value.AggregateId);
            writer.WritePropertyName("group");
            writer.WriteStringValue(value.Group);
            writer.WritePropertyName("rootPartitionKey");
            writer.WriteStringValue(value.RootPartitionKey);
            writer.WriteEndObject();
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, PartitionKeys value, JsonSerializerOptions options)
        {
            writer.WritePropertyName($"{value.RootPartitionKey}@{value.Group}@{value.AggregateId}");
        }
    }
    #endregion

    [Fact]
    public async Task SerializeDeserialize_MultiProjectorPayload_Success()
    {
        // Arrange
        var payload = CreateTestMultiProjectorPayload();
        var originalState = CreateMultiProjectionState(payload);

        // Act
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _domainTypes);
        var result = await serializable.ToMultiProjectionStateAsync(_domainTypes);

        // Assert
        Assert.True(result.HasValue);
        var restoredState = result.Value;
        
        AssertMultiProjectionStatesEqual(originalState, restoredState);
        
        var originalPayload = (MultiProjectorPayload)originalState.ProjectorCommon;
        var restoredPayload = (MultiProjectorPayload)restoredState.ProjectorCommon;
        
        Assert.Equal(originalPayload.Users.Count, restoredPayload.Users.Count);
        foreach (var userId in originalPayload.Users.Keys)
        {
            Assert.True(restoredPayload.Users.ContainsKey(userId));
            var originalUser = originalPayload.Users[userId];
            var restoredUser = restoredPayload.Users[userId];
            Assert.Equal(originalUser.UserId, restoredUser.UserId);
            Assert.Equal(originalUser.Name, restoredUser.Name);
            Assert.Equal(originalUser.Email, restoredUser.Email);
            Assert.Equal(originalUser.IsConfirmed, restoredUser.IsConfirmed);
        }
        
        Assert.Equal(originalPayload.Carts.Count, restoredPayload.Carts.Count);
        foreach (var cartId in originalPayload.Carts.Keys)
        {
            Assert.True(restoredPayload.Carts.ContainsKey(cartId));
            var originalCart = originalPayload.Carts[cartId];
            var restoredCart = restoredPayload.Carts[cartId];
            Assert.Equal(originalCart.CartId, restoredCart.CartId);
            Assert.Equal(originalCart.UserId, restoredCart.UserId);
            Assert.Equal(originalCart.PaymentMethod, restoredCart.PaymentMethod);
            
            Assert.Equal(originalCart.Items.Count, restoredCart.Items.Count);
            for (var i = 0; i < originalCart.Items.Count; i++)
            {
                var originalItem = originalCart.Items[i];
                var restoredItem = restoredCart.Items[i];
                Assert.Equal(originalItem.ItemId, restoredItem.ItemId);
                Assert.Equal(originalItem.Name, restoredItem.Name);
                Assert.Equal(originalItem.Quantity, restoredItem.Quantity);
                Assert.Equal(originalItem.Price, restoredItem.Price);
            }
        }
    }

    [Fact(Skip = "PartitionKeys as dictionary keys requires custom JSON handling in SerializableMultiProjectionState")]
    public async Task SerializeDeserialize_AggregateListProjector_Success()
    {
        // Arrange
        var payload = CreateTestAggregateListProjector();
        var originalState = CreateMultiProjectionState(payload);

        // Act
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _domainTypes);
        var result = await serializable.ToMultiProjectionStateAsync(_domainTypes);

        // Assert
        Assert.True(result.HasValue);
        var restoredState = result.Value;
        
        AssertMultiProjectionStatesEqual(originalState, restoredState);
        
        var originalPayload = (AggregateListProjector<BranchProjector>)originalState.ProjectorCommon;
        var restoredPayload = (AggregateListProjector<BranchProjector>)restoredState.ProjectorCommon;
        
        Assert.Equal(originalPayload.Aggregates.Count, restoredPayload.Aggregates.Count);
        
        foreach (var partitionKeys in originalPayload.Aggregates.Keys)
        {
            Assert.True(restoredPayload.Aggregates.ContainsKey(partitionKeys));
            
            var originalAggregate = originalPayload.Aggregates[partitionKeys];
            var restoredAggregate = restoredPayload.Aggregates[partitionKeys];
            
            Assert.Equal(originalAggregate.Version, restoredAggregate.Version);
            Assert.Equal(originalAggregate.PartitionKeys.RootPartitionKey, restoredAggregate.PartitionKeys.RootPartitionKey);
            Assert.Equal(originalAggregate.PartitionKeys.AggregateId, restoredAggregate.PartitionKeys.AggregateId);
            Assert.Equal(originalAggregate.PartitionKeys.Group, restoredAggregate.PartitionKeys.Group);
            
            Assert.Equal(
                originalAggregate.GetPayload().GetType().FullName,
                restoredAggregate.GetPayload().GetType().FullName);
            
            if (originalAggregate.GetPayload() is Branch originalBranch && 
                restoredAggregate.GetPayload() is Branch restoredBranch)
            {
                Assert.Equal(originalBranch.Name, restoredBranch.Name);
            }
        }
    }

    [Fact]
    public async Task Deserialize_VersionMismatch_ReturnsNone()
    {
        // Arrange
        var payload = CreateTestMultiProjectorPayload();
        var originalState = CreateMultiProjectionState(payload);
        
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _domainTypes);
        
        var modifiedSerializable = serializable with { PayloadVersion = "999.0.0.0" };
        
        // Act
        var result = await modifiedSerializable.ToMultiProjectionStateAsync(_domainTypes);
        
        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task Deserialize_TypeNameMismatch_ReturnsNone()
    {
        // Arrange
        var payload = CreateTestMultiProjectorPayload();
        var originalState = CreateMultiProjectionState(payload);
        
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _domainTypes);
        
        var modifiedSerializable = serializable with { PayloadTypeName = "Some.Invalid.Type, Assembly" };
        
        // Act
        var result = await modifiedSerializable.ToMultiProjectionStateAsync(_domainTypes);
        
        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task Deserialize_NullCompressedPayload_ReturnsNone()
    {
        // Arrange
        var payload = CreateTestMultiProjectorPayload();
        var originalState = CreateMultiProjectionState(payload);
        
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _domainTypes);
        
        var modifiedSerializable = serializable with { CompressedPayloadJson = null };
        
        // Act
        var result = await modifiedSerializable.ToMultiProjectionStateAsync(_domainTypes);
        
        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task SerializeDeserialize_LargeData_Success()
    {
        var users = ImmutableDictionary<Guid, MultiProjectorPayload.User>.Empty;
        var carts = ImmutableDictionary<Guid, MultiProjectorPayload.Cart>.Empty;
        
        for (var i = 0; i < 100; i++)
        {
            var userId = Guid.NewGuid();
            users = users.Add(
                userId, 
                new MultiProjectorPayload.User(
                    userId, 
                    $"User{i}", 
                    $"user{i}@example.com", 
                    i % 2 == 0));
            
            var cartId = Guid.NewGuid();
            var items = ImmutableList<MultiProjectorPayload.Item>.Empty;
            
            for (var j = 0; j < 20; j++)
            {
                items = items.Add(
                    new MultiProjectorPayload.Item(
                        Guid.NewGuid(), 
                        $"Item-{i}-{j}", 
                        j + 1, 
                        (j + 1) * 10.0m));
            }
            
            carts = carts.Add(
                cartId, 
                new MultiProjectorPayload.Cart(
                    cartId, 
                    userId, 
                    items, 
                    i % 3 == 0 ? "CreditCard" : i % 3 == 1 ? "PayPal" : "BankTransfer"));
        }
        
        var largePayload = new MultiProjectorPayload(users, carts);
        var originalState = CreateMultiProjectionState(largePayload);
        
        // Act
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _domainTypes);
        var result = await serializable.ToMultiProjectionStateAsync(_domainTypes);
        
        // Assert
        Assert.True(result.HasValue);
        var restoredState = result.Value;
        
        AssertMultiProjectionStatesEqual(originalState, restoredState);
        
        var originalPayload = (MultiProjectorPayload)originalState.ProjectorCommon;
        var restoredPayload = (MultiProjectorPayload)restoredState.ProjectorCommon;
        
        Assert.Equal(100, originalPayload.Users.Count);
        Assert.Equal(100, restoredPayload.Users.Count);
        Assert.Equal(100, originalPayload.Carts.Count);
        Assert.Equal(100, restoredPayload.Carts.Count);
        
        foreach (var userId in originalPayload.Users.Keys.Take(10))
        {
            Assert.True(restoredPayload.Users.ContainsKey(userId));
            Assert.Equal(
                originalPayload.Users[userId].Name,
                restoredPayload.Users[userId].Name);
        }
        
        foreach (var cartId in originalPayload.Carts.Keys.Take(10))
        {
            Assert.True(restoredPayload.Carts.ContainsKey(cartId));
            Assert.Equal(
                originalPayload.Carts[cartId].Items.Count,
                restoredPayload.Carts[cartId].Items.Count);
        }
    }
}
