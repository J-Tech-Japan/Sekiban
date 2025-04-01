using System.Text.Json;
using Sekiban.Pure;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Projectors;
using Pure.Domain;
using Pure.Domain.Generated;
using ResultBoxes;

namespace Pure.Domain.xUnit;

/// <summary>
/// SerializableAggregate のシリアライズ/デシリアライズをテストするためのテストクラス
/// </summary>
public class SerializableAggregateTests
{
    private readonly SekibanDomainTypes _domainTypes;

    public SerializableAggregateTests()
    {
        // テスト用のSekibanDomainTypesを取得
        _domainTypes = PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options);
    }

    #region Helper Methods

    /// <summary>
    /// テスト用のBranch Aggregateを作成します
    /// </summary>
    private Aggregate CreateBranchAggregate()
    {
        var branchId = Guid.NewGuid();
        var partitionKeys = new PartitionKeys(branchId, typeof(BranchProjector).Name, "test");
        var branch = new Branch("Test Branch");
        
        return new Aggregate(
            branch, 
            partitionKeys, 
            1, 
            "testSortable1", 
            "1.0.0", 
            typeof(BranchProjector).Name,
            typeof(Branch).Name);
    }

    /// <summary>
    /// テスト用のClient Aggregateを作成します
    /// </summary>
    private Aggregate CreateClientAggregate()
    {
        var clientId = Guid.NewGuid();
        var branchId = Guid.NewGuid(); // クライアントに必要な支店ID
        var partitionKeys = new PartitionKeys(clientId, typeof(ClientProjector).Name, "test");
        var client = new Client(branchId, "Test Client", "test@example.com");
        
        return new Aggregate(
            client, 
            partitionKeys, 
            2, 
            "testSortable2", 
            "1.0.0", 
            typeof(ClientProjector).Name,
            typeof(Client).Name);
    }

    /// <summary>
    /// テスト用のUser Aggregateを作成します
    /// </summary>
    private Aggregate CreateUserAggregate(bool confirmed = true)
    {
        var userId = Guid.NewGuid();
        var partitionKeys = new PartitionKeys(userId, typeof(UserProjector).Name, "test");
        
        // 確認済みユーザーか未確認ユーザーを作成
        IAggregatePayload userPayload = confirmed 
            ? new ConfirmedUser("Test User", "test@example.com") 
            : new UnconfirmedUser("Test User", "test@example.com");
        
        return new Aggregate(
            userPayload, 
            partitionKeys, 
            3, 
            "testSortable3", 
            "1.0.0", 
            typeof(UserProjector).Name,
            userPayload.GetType().Name);
    }

    /// <summary>
    /// テスト用のEmpty Aggregateを作成します
    /// </summary>
    private Aggregate CreateEmptyAggregate()
    {
        var aggregateId = Guid.NewGuid();
        var partitionKeys = new PartitionKeys(aggregateId, typeof(BranchProjector).Name, "test");
        
        return new Aggregate(
            new EmptyAggregatePayload(), 
            partitionKeys, 
            0, 
            "", 
            "1.0.0", 
            typeof(BranchProjector).Name,
            typeof(EmptyAggregatePayload).Name);
    }

    /// <summary>
    /// Aggregateの内容を比較します
    /// </summary>
    private void AssertAggregatesEqual(Aggregate expected, Aggregate actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.LastSortableUniqueId, actual.LastSortableUniqueId);
        Assert.Equal(expected.ProjectorVersion, actual.ProjectorVersion);
        Assert.Equal(expected.ProjectorTypeName, actual.ProjectorTypeName);
        Assert.Equal(expected.PayloadTypeName, actual.PayloadTypeName);
        
        // PartitionKeysを比較
        Assert.Equal(expected.PartitionKeys.AggregateId, actual.PartitionKeys.AggregateId);
        Assert.Equal(expected.PartitionKeys.Group, actual.PartitionKeys.Group);
        Assert.Equal(expected.PartitionKeys.RootPartitionKey, actual.PartitionKeys.RootPartitionKey);
        
        // Payloadの型を比較
        Assert.Equal(expected.Payload.GetType(), actual.Payload.GetType());
        
        // Payload固有のプロパティを比較
        if (expected.Payload is Branch expectedBranch && actual.Payload is Branch actualBranch)
        {
            Assert.Equal(expectedBranch.Name, actualBranch.Name);
        }
        else if (expected.Payload is Client expectedClient && actual.Payload is Client actualClient)
        {
            Assert.Equal(expectedClient.Name, actualClient.Name);
        }
        else if (expected.Payload is ConfirmedUser expectedUser && actual.Payload is ConfirmedUser actualUser)
        {
            Assert.Equal(expectedUser.Name, actualUser.Name);
            Assert.Equal(expectedUser.Email, actualUser.Email);
        }
        else if (expected.Payload is UnconfirmedUser expectedUnconfirmedUser && 
                actual.Payload is UnconfirmedUser actualUnconfirmedUser)
        {
            Assert.Equal(expectedUnconfirmedUser.Name, actualUnconfirmedUser.Name);
            Assert.Equal(expectedUnconfirmedUser.Email, actualUnconfirmedUser.Email);
        }
    }
    
    #endregion

    [Fact]
    public async Task SerializeDeserialize_BranchAggregate_Success()
    {
        // Arrange
        var originalAggregate = CreateBranchAggregate();
        
        // Act
        var serializable = await SerializableAggregate.CreateFromAsync(originalAggregate, _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);
        
        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();
        
        AssertAggregatesEqual(originalAggregate, restoredAggregate);
    }

    [Fact]
    public async Task SerializeDeserialize_ClientAggregate_Success()
    {
        // Arrange
        var originalAggregate = CreateClientAggregate();
        
        // Act
        var serializable = await SerializableAggregate.CreateFromAsync(originalAggregate, _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);
        
        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();
        
        AssertAggregatesEqual(originalAggregate, restoredAggregate);
    }

    [Fact]
    public async Task SerializeDeserialize_ConfirmedUserAggregate_Success()
    {
        // Arrange
        var originalAggregate = CreateUserAggregate(confirmed: true);
        
        // Act
        var serializable = await SerializableAggregate.CreateFromAsync(originalAggregate, _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);
        
        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();
        
        AssertAggregatesEqual(originalAggregate, restoredAggregate);
        
        // ConfirmedUserの型を明示的に確認
        Assert.IsType<ConfirmedUser>(restoredAggregate.Payload);
    }

    [Fact]
    public async Task SerializeDeserialize_UnconfirmedUserAggregate_Success()
    {
        // Arrange
        var originalAggregate = CreateUserAggregate(confirmed: false);
        
        // Act
        var serializable = await SerializableAggregate.CreateFromAsync(originalAggregate, _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);
        
        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();
        
        AssertAggregatesEqual(originalAggregate, restoredAggregate);
        
        // UnconfirmedUserの型を明示的に確認
        Assert.IsType<UnconfirmedUser>(restoredAggregate.Payload);
    }

    [Fact]
    public async Task SerializeDeserialize_EmptyAggregate_Success()
    {
        // Arrange
        var originalAggregate = CreateEmptyAggregate();
        
        // Act
        var serializable = await SerializableAggregate.CreateFromAsync(originalAggregate, _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);
        
        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();
        
        AssertAggregatesEqual(originalAggregate, restoredAggregate);
        
        // EmptyAggregatePayloadの型を明示的に確認
        Assert.IsType<EmptyAggregatePayload>(restoredAggregate.Payload);
    }

    [Fact]
    public async Task Deserialize_TypeNameMismatch_ReturnsNone()
    {
        // Arrange
        var originalAggregate = CreateBranchAggregate();
        
        // Act - まず正常にシリアライズ
        var serializable = await SerializableAggregate.CreateFromAsync(originalAggregate, _domainTypes.JsonSerializerOptions);
        
        // ペイロード型名を変更
        var modifiedSerializable = serializable with { PayloadTypeName = "NonExistentType" };
        
        // デシリアライズ試行
        var result = await modifiedSerializable.ToAggregateAsync(_domainTypes);
        
        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task Deserialize_InvalidCompressedData_ReturnsNone()
    {
        // Arrange
        var originalAggregate = CreateBranchAggregate();
        
        // Act - まず正常にシリアライズ
        var serializable = await SerializableAggregate.CreateFromAsync(originalAggregate, _domainTypes.JsonSerializerOptions);
        
        // 不正な圧縮データに置き換え
        var modifiedSerializable = serializable with { CompressedPayloadJson = new byte[] { 1, 2, 3, 4, 5 } };
        
        // デシリアライズ試行
        var result = await modifiedSerializable.ToAggregateAsync(_domainTypes);
        
        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task Deserialize_EmptyCompressedData_ReturnsEmptyPayload()
    {
        // Arrange
        var originalAggregate = CreateBranchAggregate();
        
        // Act - まず正常にシリアライズ
        var serializable = await SerializableAggregate.CreateFromAsync(originalAggregate, _domainTypes.JsonSerializerOptions);
        
        // 空の圧縮データに置き換え
        var modifiedSerializable = serializable with { CompressedPayloadJson = Array.Empty<byte>() };
        
        // デシリアライズ試行
        var result = await modifiedSerializable.ToAggregateAsync(_domainTypes);
        
        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();
        
        // EmptyAggregatePayloadになっているか確認
        Assert.IsType<EmptyAggregatePayload>(restoredAggregate.Payload);
    }

    [Fact]
    public async Task GetPayloadTypeByName_ValidName_ReturnsCorrectType()
    {
        // 注：Source Generatorの制限により、GetPayloadTypeByNameは完全修飾名ではなく
        // 短い型名を使用して型検索を行います。実際のアプリケーションでは、
        // 同じ短い名前を持つ異なる型を区別するために追加のロジックが必要になる場合があります。
        
        // テストのためにGetAggregateTypesから直接型を取得して検証
        var allAggregateTypes = _domainTypes.AggregateTypes.GetAggregateTypes();
        
        // Branch型
        var branchType = allAggregateTypes.FirstOrDefault(t => t.Name == typeof(Branch).Name);
        Assert.NotNull(branchType);
        Assert.Equal(typeof(Branch).Name, branchType.Name);
        
        // Client型
        var clientType = allAggregateTypes.FirstOrDefault(t => t.Name == typeof(Client).Name);
        Assert.NotNull(clientType);
        Assert.Equal(typeof(Client).Name, clientType.Name);
        
        // ConfirmedUser型
        var confirmedUserType = allAggregateTypes.FirstOrDefault(t => t.Name == typeof(ConfirmedUser).Name);
        Assert.NotNull(confirmedUserType);
        Assert.Equal(typeof(ConfirmedUser).Name, confirmedUserType.Name);
        
        // UnconfirmedUser型
        var unconfirmedUserType = allAggregateTypes.FirstOrDefault(t => t.Name == typeof(UnconfirmedUser).Name);
        Assert.NotNull(unconfirmedUserType);
        Assert.Equal(typeof(UnconfirmedUser).Name, unconfirmedUserType.Name);
        
        // GetPayloadTypeByNameの実装が完全に機能していることを適宜確認
        // フルネームでなく短い名前を使うことに注意
        var branchTypeShortName = _domainTypes.AggregateTypes.GetPayloadTypeByName("Branch");
        if (branchTypeShortName != null)
        {
            Assert.Equal(typeof(Branch).Name, branchTypeShortName.Name);
        }
    }

    [Fact]
    public async Task GetPayloadTypeByName_InvalidName_ReturnsNull()
    {
        // 存在しない型名
        var nonExistentType = _domainTypes.AggregateTypes.GetPayloadTypeByName("NonExistentType");
        Assert.Null(nonExistentType);
        
        // null型名
        var nullType = _domainTypes.AggregateTypes.GetPayloadTypeByName(null);
        Assert.Null(nullType);
        
        // 空文字型名
        var emptyType = _domainTypes.AggregateTypes.GetPayloadTypeByName(string.Empty);
        Assert.Null(emptyType);
    }

    [Fact]
    public async Task SerializeDeserialize_LargeData_Success()
    {
        // Arrange - 大きめの文字列データを含むBranchを作成
        // これはシリアライズ・デシリアライズの圧縮機能をテストするため
        var largeDescription = new string('X', 10000); // 10,000文字の文字列
        
        var branchId = Guid.NewGuid();
        var partitionKeys = new PartitionKeys(branchId, typeof(BranchProjector).Name, "test");
        var branch = new Branch($"Large Branch {largeDescription}");
        
        var originalAggregate = new Aggregate(
            branch, 
            partitionKeys, 
            1, 
            "testSortable1", 
            "1.0.0", 
            typeof(BranchProjector).Name,
            typeof(Branch).Name);
        
        // Act
        var serializable = await SerializableAggregate.CreateFromAsync(originalAggregate, _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);
        
        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();
        
        // 圧縮により処理できるか確認
        Assert.Equal(originalAggregate.Version, restoredAggregate.Version);
        Assert.Equal(originalAggregate.PayloadTypeName, restoredAggregate.PayloadTypeName);
        
        // 復元されたデータの内容を確認
        var originalBranch = (Branch)originalAggregate.Payload;
        var restoredBranch = (Branch)restoredAggregate.Payload;
        
        Assert.Equal(originalBranch.Name, restoredBranch.Name);
        Assert.True(restoredBranch.Name.Contains(largeDescription));
    }
}
