using Pure.Domain.Generated;
using Sekiban.Pure;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
namespace Pure.Domain.xUnit;

/// <summary>
///     Test class for testing serialization/deserialization of SerializableAggregate
/// </summary>
public class SerializableAggregateTests
{
    private readonly SekibanDomainTypes _domainTypes;

    public SerializableAggregateTests() =>
        _domainTypes = PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options);

    [Fact]
    public async Task SerializeDeserialize_BranchAggregate_Success()
    {
        // Arrange
        var originalAggregate = CreateBranchAggregate();

        // Act
        var serializable = await SerializableAggregate.CreateFromAsync(
            originalAggregate,
            _domainTypes.JsonSerializerOptions);
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
        var serializable = await SerializableAggregate.CreateFromAsync(
            originalAggregate,
            _domainTypes.JsonSerializerOptions);
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
        var serializable = await SerializableAggregate.CreateFromAsync(
            originalAggregate,
            _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);

        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();

        AssertAggregatesEqual(originalAggregate, restoredAggregate);

        Assert.IsType<ConfirmedUser>(restoredAggregate.Payload);
    }

    [Fact]
    public async Task SerializeDeserialize_UnconfirmedUserAggregate_Success()
    {
        // Arrange
        var originalAggregate = CreateUserAggregate(confirmed: false);

        // Act
        var serializable = await SerializableAggregate.CreateFromAsync(
            originalAggregate,
            _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);

        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();

        AssertAggregatesEqual(originalAggregate, restoredAggregate);

        Assert.IsType<UnconfirmedUser>(restoredAggregate.Payload);
    }

    [Fact]
    public async Task SerializeDeserialize_EmptyAggregate_Success()
    {
        // Arrange
        var originalAggregate = CreateEmptyAggregate();

        // Act
        var serializable = await SerializableAggregate.CreateFromAsync(
            originalAggregate,
            _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);

        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();

        AssertAggregatesEqual(originalAggregate, restoredAggregate);

        Assert.IsType<EmptyAggregatePayload>(restoredAggregate.Payload);
    }

    [Fact]
    public async Task Deserialize_TypeNameMismatch_ReturnsNone()
    {
        // Arrange
        var originalAggregate = CreateBranchAggregate();

        var serializable = await SerializableAggregate.CreateFromAsync(
            originalAggregate,
            _domainTypes.JsonSerializerOptions);

        var modifiedSerializable = serializable with { PayloadTypeName = "NonExistentType" };

        var result = await modifiedSerializable.ToAggregateAsync(_domainTypes);

        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task Deserialize_InvalidCompressedData_ReturnsNone()
    {
        // Arrange
        var originalAggregate = CreateBranchAggregate();

        var serializable = await SerializableAggregate.CreateFromAsync(
            originalAggregate,
            _domainTypes.JsonSerializerOptions);

        var modifiedSerializable = serializable with { CompressedPayloadJson = new byte[] { 1, 2, 3, 4, 5 } };

        var result = await modifiedSerializable.ToAggregateAsync(_domainTypes);

        // Assert
        Assert.False(result.HasValue);
    }

    [Fact]
    public async Task Deserialize_EmptyCompressedData_ReturnsEmptyPayload()
    {
        // Arrange
        var originalAggregate = CreateBranchAggregate();

        var serializable = await SerializableAggregate.CreateFromAsync(
            originalAggregate,
            _domainTypes.JsonSerializerOptions);

        var modifiedSerializable = serializable with { CompressedPayloadJson = Array.Empty<byte>() };

        var result = await modifiedSerializable.ToAggregateAsync(_domainTypes);

        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();

        Assert.IsType<EmptyAggregatePayload>(restoredAggregate.Payload);
    }

    [Fact]
    public async Task GetPayloadTypeByName_ValidName_ReturnsCorrectType()
    {

        var allAggregateTypes = _domainTypes.AggregateTypes.GetAggregateTypes();

        var branchType = allAggregateTypes.FirstOrDefault(t => t.Name == typeof(Branch).Name);
        Assert.NotNull(branchType);
        Assert.Equal(typeof(Branch).Name, branchType.Name);

        var clientType = allAggregateTypes.FirstOrDefault(t => t.Name == typeof(Client).Name);
        Assert.NotNull(clientType);
        Assert.Equal(typeof(Client).Name, clientType.Name);

        var confirmedUserType = allAggregateTypes.FirstOrDefault(t => t.Name == typeof(ConfirmedUser).Name);
        Assert.NotNull(confirmedUserType);
        Assert.Equal(typeof(ConfirmedUser).Name, confirmedUserType.Name);

        var unconfirmedUserType = allAggregateTypes.FirstOrDefault(t => t.Name == typeof(UnconfirmedUser).Name);
        Assert.NotNull(unconfirmedUserType);
        Assert.Equal(typeof(UnconfirmedUser).Name, unconfirmedUserType.Name);

        var branchTypeShortName = _domainTypes.AggregateTypes.GetPayloadTypeByName("Branch");
        if (branchTypeShortName != null)
        {
            Assert.Equal(typeof(Branch).Name, branchTypeShortName.Name);
        }
    }

    [Fact]
    public async Task GetPayloadTypeByName_InvalidName_ReturnsNull()
    {
        var nonExistentType = _domainTypes.AggregateTypes.GetPayloadTypeByName("NonExistentType");
        Assert.Null(nonExistentType);

        var nullType = _domainTypes.AggregateTypes.GetPayloadTypeByName(null);
        Assert.Null(nullType);

        var emptyType = _domainTypes.AggregateTypes.GetPayloadTypeByName(string.Empty);
        Assert.Null(emptyType);
    }

    [Fact]
    public async Task SerializeDeserialize_LargeData_Success()
    {
        var largeDescription = new string('X', 10000);

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
        var serializable = await SerializableAggregate.CreateFromAsync(
            originalAggregate,
            _domainTypes.JsonSerializerOptions);
        var result = await serializable.ToAggregateAsync(_domainTypes);

        // Assert
        Assert.True(result.HasValue);
        var restoredAggregate = result.GetValue();

        Assert.Equal(originalAggregate.Version, restoredAggregate.Version);
        Assert.Equal(originalAggregate.PayloadTypeName, restoredAggregate.PayloadTypeName);

        var originalBranch = (Branch)originalAggregate.Payload;
        var restoredBranch = (Branch)restoredAggregate.Payload;

        Assert.Equal(originalBranch.Name, restoredBranch.Name);
        Assert.True(restoredBranch.Name.Contains(largeDescription));
    }

    #region Helper Methods
    /// <summary>
    ///     Creates a Branch Aggregate for testing
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
    ///     Creates a Client Aggregate for testing
    /// </summary>
    private Aggregate CreateClientAggregate()
    {
        var clientId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
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
    ///     Creates a User Aggregate for testing
    /// </summary>
    private Aggregate CreateUserAggregate(bool confirmed = true)
    {
        var userId = Guid.NewGuid();
        var partitionKeys = new PartitionKeys(userId, typeof(UserProjector).Name, "test");

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
    ///     Creates an Empty Aggregate for testing
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
    ///     Compares the contents of Aggregates
    /// </summary>
    private void AssertAggregatesEqual(Aggregate expected, Aggregate actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.LastSortableUniqueId, actual.LastSortableUniqueId);
        Assert.Equal(expected.ProjectorVersion, actual.ProjectorVersion);
        Assert.Equal(expected.ProjectorTypeName, actual.ProjectorTypeName);
        Assert.Equal(expected.PayloadTypeName, actual.PayloadTypeName);

        Assert.Equal(expected.PartitionKeys.AggregateId, actual.PartitionKeys.AggregateId);
        Assert.Equal(expected.PartitionKeys.Group, actual.PartitionKeys.Group);
        Assert.Equal(expected.PartitionKeys.RootPartitionKey, actual.PartitionKeys.RootPartitionKey);

        Assert.Equal(expected.Payload.GetType(), actual.Payload.GetType());

        if (expected.Payload is Branch expectedBranch && actual.Payload is Branch actualBranch)
        {
            Assert.Equal(expectedBranch.Name, actualBranch.Name);
        } else if (expected.Payload is Client expectedClient && actual.Payload is Client actualClient)
        {
            Assert.Equal(expectedClient.Name, actualClient.Name);
        } else if (expected.Payload is ConfirmedUser expectedUser && actual.Payload is ConfirmedUser actualUser)
        {
            Assert.Equal(expectedUser.Name, actualUser.Name);
            Assert.Equal(expectedUser.Email, actualUser.Email);
        } else if (expected.Payload is UnconfirmedUser expectedUnconfirmedUser &&
            actual.Payload is UnconfirmedUser actualUnconfirmedUser)
        {
            Assert.Equal(expectedUnconfirmedUser.Name, actualUnconfirmedUser.Name);
            Assert.Equal(expectedUnconfirmedUser.Email, actualUnconfirmedUser.Email);
        }
    }
    #endregion
}
