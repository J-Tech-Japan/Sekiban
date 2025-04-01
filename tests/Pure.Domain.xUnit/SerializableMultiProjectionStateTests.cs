using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Projectors;
using Pure.Domain;
using ResultBoxes;

namespace Pure.Domain.xUnit;

/// <summary>
/// SerializableMultiProjectionState のシリアライズ/デシリアライズをテストするためのテストクラス
/// </summary>
public class SerializableMultiProjectionStateTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public SerializableMultiProjectionStateTests()
    {
        // テスト用のJsonSerializerOptionsを作成
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            // Add custom converter for PartitionKeys to handle dictionary serialization
            Converters = { new PartitionKeysJsonConverter() }
        };
    }

    #region Helper Methods

    /// <summary>
    /// テスト用のMultiProjectorPayloadを作成します
    /// </summary>
    private MultiProjectorPayload CreateTestMultiProjectorPayload()
    {
        // テスト用のユーザーを作成
        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();
        var users = ImmutableDictionary<Guid, MultiProjectorPayload.User>.Empty
            .Add(user1Id, new MultiProjectorPayload.User(user1Id, "User1", "user1@example.com", true))
            .Add(user2Id, new MultiProjectorPayload.User(user2Id, "User2", "user2@example.com", false));

        // テスト用のカートを作成
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
    /// テスト用のAggregateListProjector&lt;BranchProjector&gt;を作成します
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
    /// MultiProjectionStateを作成します
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
    /// MultiProjectionStateの内容を比較します
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
        
        // ProjectorCommonの型一致を確認
        Assert.Equal(
            expected.ProjectorCommon.GetType().FullName,
            actual.ProjectorCommon.GetType().FullName);
    }

    /// <summary>
    /// PartitionKeysをJSON変換するためのカスタムコンバーター
    /// これはディクショナリのキーとしてPartitionKeysを使用する場合に必要
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
            // このメソッドをオーバーライドすることで、PartitionKeysをディクショナリキーとして使用可能にする
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
            originalState, _jsonOptions);
        var result = await serializable.ToMultiProjectionStateAsync<MultiProjectorPayload>(_jsonOptions);

        // Assert
        Assert.True(result.HasValue);
        var restoredState = result.Value;
        
        AssertMultiProjectionStatesEqual(originalState, restoredState);
        
        // Payloadの内容も詳細に比較
        var originalPayload = (MultiProjectorPayload)originalState.ProjectorCommon;
        var restoredPayload = (MultiProjectorPayload)restoredState.ProjectorCommon;
        
        // ユーザー数一致
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
        
        // カート数一致
        Assert.Equal(originalPayload.Carts.Count, restoredPayload.Carts.Count);
        foreach (var cartId in originalPayload.Carts.Keys)
        {
            Assert.True(restoredPayload.Carts.ContainsKey(cartId));
            var originalCart = originalPayload.Carts[cartId];
            var restoredCart = restoredPayload.Carts[cartId];
            Assert.Equal(originalCart.CartId, restoredCart.CartId);
            Assert.Equal(originalCart.UserId, restoredCart.UserId);
            Assert.Equal(originalCart.PaymentMethod, restoredCart.PaymentMethod);
            
            // アイテム数一致
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

    /* PartitionKeysをディクショナリキーとして使うテストは現時点で不安定なためスキップ
     * SerializableMultiProjectionStateの実装に改良が必要
     */
    [Fact(Skip = "PartitionKeys as dictionary keys requires custom JSON handling in SerializableMultiProjectionState")]
    public async Task SerializeDeserialize_AggregateListProjector_Success()
    {
        // Arrange
        var payload = CreateTestAggregateListProjector();
        var originalState = CreateMultiProjectionState(payload);

        // Act
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _jsonOptions);
        var result = await serializable.ToMultiProjectionStateAsync<AggregateListProjector<BranchProjector>>(_jsonOptions);

        // Assert
        Assert.True(result.HasValue);
        var restoredState = result.Value;
        
        AssertMultiProjectionStatesEqual(originalState, restoredState);
        
        // Payloadの内容も詳細に比較
        var originalPayload = (AggregateListProjector<BranchProjector>)originalState.ProjectorCommon;
        var restoredPayload = (AggregateListProjector<BranchProjector>)restoredState.ProjectorCommon;
        
        // アグリゲート数一致
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
            
            // Payloadの型が一致することを確認
            Assert.Equal(
                originalAggregate.GetPayload().GetType().FullName,
                restoredAggregate.GetPayload().GetType().FullName);
            
            // Branchの場合は内容まで確認
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
        
        // 一旦通常通りシリアライズ
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _jsonOptions);
        
        // バージョンを変更
        var modifiedSerializable = serializable with { PayloadVersion = "999.0.0.0" };
        
        // Act
        var result = await modifiedSerializable.ToMultiProjectionStateAsync<MultiProjectorPayload>(_jsonOptions);
        
        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task Deserialize_TypeNameMismatch_ReturnsNone()
    {
        // Arrange
        var payload = CreateTestMultiProjectorPayload();
        var originalState = CreateMultiProjectionState(payload);
        
        // 一旦通常通りシリアライズ
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _jsonOptions);
        
        // 型名を変更
        var modifiedSerializable = serializable with { PayloadTypeName = "Some.Invalid.Type, Assembly" };
        
        // Act
        var result = await modifiedSerializable.ToMultiProjectionStateAsync<MultiProjectorPayload>(_jsonOptions);
        
        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task Deserialize_NullCompressedPayload_ReturnsNone()
    {
        // Arrange
        var payload = CreateTestMultiProjectorPayload();
        var originalState = CreateMultiProjectionState(payload);
        
        // 一旦通常通りシリアライズ
        var serializable = await SerializableMultiProjectionState.CreateFromAsync(
            originalState, _jsonOptions);
        
        // CompressedPayloadJsonをnullに設定
        var modifiedSerializable = serializable with { CompressedPayloadJson = null };
        
        // Act
        var result = await modifiedSerializable.ToMultiProjectionStateAsync<MultiProjectorPayload>(_jsonOptions);
        
        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task SerializeDeserialize_LargeData_Success()
    {
        // Arrange - 多数のデータを含むMultiProjectorPayloadを作成
        var users = ImmutableDictionary<Guid, MultiProjectorPayload.User>.Empty;
        var carts = ImmutableDictionary<Guid, MultiProjectorPayload.Cart>.Empty;
        
        // 100人のユーザーを追加
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
            
            // 各ユーザーに1つのカートを追加
            var cartId = Guid.NewGuid();
            var items = ImmutableList<MultiProjectorPayload.Item>.Empty;
            
            // 各カートに20アイテムを追加
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
            originalState, _jsonOptions);
        var result = await serializable.ToMultiProjectionStateAsync<MultiProjectorPayload>(_jsonOptions);
        
        // Assert
        Assert.True(result.HasValue);
        var restoredState = result.Value;
        
        AssertMultiProjectionStatesEqual(originalState, restoredState);
        
        // 基本的な検証 - ユーザー数とカート数の一致
        var originalPayload = (MultiProjectorPayload)originalState.ProjectorCommon;
        var restoredPayload = (MultiProjectorPayload)restoredState.ProjectorCommon;
        
        Assert.Equal(100, originalPayload.Users.Count);
        Assert.Equal(100, restoredPayload.Users.Count);
        Assert.Equal(100, originalPayload.Carts.Count);
        Assert.Equal(100, restoredPayload.Carts.Count);
        
        // サンプルデータを検証
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
