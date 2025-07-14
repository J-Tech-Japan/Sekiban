using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sekiban.Pure;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Projectors;
using Pure.Domain;
using Pure.Domain.Generated;
using ResultBoxes;

namespace Pure.Domain.xUnit;

/// <summary>
/// Test class for testing serialization/deserialization of SerializableAggregateListProjector
/// </summary>
public class SerializableAggregateListProjectorTests
{
    private readonly SekibanDomainTypes _domainTypes;

    public SerializableAggregateListProjectorTests()
    {
        _domainTypes = PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options);
    }

    #region Helper Methods

    /// <summary>
    /// Creates a Branch aggregate list projector for testing
    /// </summary>
    private AggregateListProjector<BranchProjector> CreateTestBranchListProjector()
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
    /// Creates a Client aggregate list projector for testing
    /// </summary>
    private AggregateListProjector<ClientProjector> CreateTestClientListProjector()
    {
        var client1Id = Guid.NewGuid();
        var client2Id = Guid.NewGuid();
        var branch1Id = Guid.NewGuid();
        var branch2Id = Guid.NewGuid();
        
        var partitionKeys1 = new PartitionKeys(client1Id, typeof(ClientProjector).Name, "test");
        var partitionKeys2 = new PartitionKeys(client2Id, typeof(ClientProjector).Name, "test");
        
        var client1 = new Client(branch1Id, "Client A", "clientA@example.com");
        var client2 = new Client(branch2Id, "Client B", "clientB@example.com");
        
        var aggregate1 = new Aggregate(
            client1, 
            partitionKeys1, 
            1, 
            "testSortable1", 
            "1.0.0", 
            typeof(ClientProjector).Name,
            typeof(Client).Name);
        var aggregate2 = new Aggregate(
            client2, 
            partitionKeys2, 
            2, 
            "testSortable2", 
            "1.0.0", 
            typeof(ClientProjector).Name,
            typeof(Client).Name);
        
        var aggregates = ImmutableDictionary<PartitionKeys, Aggregate>.Empty
            .Add(partitionKeys1, aggregate1)
            .Add(partitionKeys2, aggregate2);
        
        return new AggregateListProjector<ClientProjector>(aggregates);
    }

    /// <summary>
    /// Creates a large number of Branch aggregate list projectors for testing
    /// </summary>
    private AggregateListProjector<BranchProjector> CreateLargeBranchListProjector(int count = 100)
    {
        var aggregates = ImmutableDictionary<PartitionKeys, Aggregate>.Empty;
        
        for (var i = 0; i < count; i++)
        {
            var branchId = Guid.NewGuid();
            var partitionKeys = new PartitionKeys(branchId, typeof(BranchProjector).Name, "test");
            var branch = new Branch($"Branch {i}");
            
            var aggregate = new Aggregate(
                branch, 
                partitionKeys, 
                i + 1, 
                $"testSortable{i}", 
                "1.0.0", 
                typeof(BranchProjector).Name,
                typeof(Branch).Name);
            
            aggregates = aggregates.Add(partitionKeys, aggregate);
        }
        
        return new AggregateListProjector<BranchProjector>(aggregates);
    }

    /// <summary>
    /// Creates a list projector with multiple types of aggregate payloads
    /// </summary>
    private AggregateListProjector<BranchProjector> CreateMixedTypeListProjector()
    {
        var aggregates = ImmutableDictionary<PartitionKeys, Aggregate>.Empty;
        
        var branch1Id = Guid.NewGuid();
        var partitionKeys1 = new PartitionKeys(branch1Id, typeof(BranchProjector).Name, "test");
        var branch1 = new Branch("Tokyo Branch");
        var aggregate1 = new Aggregate(
            branch1, 
            partitionKeys1, 
            1, 
            "testSortable1", 
            "1.0.0", 
            typeof(BranchProjector).Name,
            typeof(Branch).Name);
        aggregates = aggregates.Add(partitionKeys1, aggregate1);
        
        var emptyId = Guid.NewGuid();
        var partitionKeys2 = new PartitionKeys(emptyId, typeof(BranchProjector).Name, "test");
        var aggregate2 = new Aggregate(
            new EmptyAggregatePayload(), 
            partitionKeys2, 
            1, 
            "testSortable2", 
            "1.0.0", 
            typeof(BranchProjector).Name,
            typeof(EmptyAggregatePayload).Name);
        aggregates = aggregates.Add(partitionKeys2, aggregate2);
        
        return new AggregateListProjector<BranchProjector>(aggregates);
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
    public async Task SerializeDeserialize_BranchListProjector_Success()
    {
        // Arrange
        var originalProjector = CreateTestBranchListProjector();
        
        // Act
        var serializedJson = await SerializableAggregateListProjector.SerializeAggregateList(originalProjector, _domainTypes);
        var serializedString = serializedJson.IsSuccess ? serializedJson.GetValue() : null;
        Assert.NotNull(serializedString);
        
        var deserializedResult = await SerializableAggregateListProjector.DeserializeAggregateList<BranchProjector>(
            serializedString!, _domainTypes);
        
        // Assert
        Assert.True(deserializedResult.IsSuccess);
        var restoredProjector = deserializedResult.GetValue();
        
        Assert.Equal(originalProjector.Aggregates.Count, restoredProjector.Aggregates.Count);
        
        foreach (var partitionKeys in originalProjector.Aggregates.Keys)
        {
            Assert.True(restoredProjector.Aggregates.ContainsKey(partitionKeys));
            
            var originalAggregate = originalProjector.Aggregates[partitionKeys];
            var restoredAggregate = restoredProjector.Aggregates[partitionKeys];
            
            Assert.Equal(originalAggregate.Version, restoredAggregate.Version);
            Assert.Equal(originalAggregate.LastSortableUniqueId, restoredAggregate.LastSortableUniqueId);
            Assert.Equal(originalAggregate.Version, restoredAggregate.Version);
            Assert.Equal(originalAggregate.ProjectorVersion, restoredAggregate.ProjectorVersion);
            Assert.Equal(originalAggregate.PayloadTypeName, restoredAggregate.PayloadTypeName);
            
            Assert.Equal(originalAggregate.PartitionKeys.AggregateId, restoredAggregate.PartitionKeys.AggregateId);
            Assert.Equal(originalAggregate.PartitionKeys.Group, restoredAggregate.PartitionKeys.Group);
            Assert.Equal(originalAggregate.PartitionKeys.RootPartitionKey, restoredAggregate.PartitionKeys.RootPartitionKey);
            
            Assert.Equal(originalAggregate.GetPayload().GetType(), restoredAggregate.GetPayload().GetType());
            
            if (originalAggregate.GetPayload() is Branch originalBranch && 
                restoredAggregate.GetPayload() is Branch restoredBranch)
            {
                Assert.Equal(originalBranch.Name, restoredBranch.Name);
            }
        }
    }
    
    [Fact]
    public async Task SerializeDeserialize_ClientListProjector_Success()
    {
        // Arrange
        var originalProjector = CreateTestClientListProjector();
        
        // Act
        var serializedJson = await SerializableAggregateListProjector.SerializeAggregateList(originalProjector, _domainTypes);
        var serializedString = serializedJson.IsSuccess ? serializedJson.GetValue() : null;
        Assert.NotNull(serializedString);
        
        var deserializedResult = await SerializableAggregateListProjector.DeserializeAggregateList<ClientProjector>(
            serializedString!, _domainTypes);
        
        // Assert
        Assert.True(deserializedResult.IsSuccess);
        var restoredProjector = deserializedResult.GetValue();
        
        Assert.Equal(originalProjector.Aggregates.Count, restoredProjector.Aggregates.Count);
        
        foreach (var partitionKeys in originalProjector.Aggregates.Keys)
        {
            Assert.True(restoredProjector.Aggregates.ContainsKey(partitionKeys));
            
            var originalAggregate = originalProjector.Aggregates[partitionKeys];
            var restoredAggregate = restoredProjector.Aggregates[partitionKeys];
            
            if (originalAggregate.GetPayload() is Client originalClient && 
                restoredAggregate.GetPayload() is Client restoredClient)
            {
                Assert.Equal(originalClient.BranchId, restoredClient.BranchId);
                Assert.Equal(originalClient.Name, restoredClient.Name);
                Assert.Equal(originalClient.Email, restoredClient.Email);
            }
        }
    }

    [Fact]
    public void CheckTypeName()
    {
        var name = typeof(AggregateListProjector<BranchProjector>).FullName;
        Assert.Contains("BranchProjector", name);
    }
    
    
    [Fact]
    public async Task SerializeDeserialize_MixedTypeListProjector_Success()
    {
        // Arrange
        var originalProjector = CreateMixedTypeListProjector();
        
        // Act
        var serializedJson = await SerializableAggregateListProjector.SerializeAggregateList(originalProjector, _domainTypes);
        var serializedString = serializedJson.IsSuccess ? serializedJson.GetValue() : null;
        Assert.NotNull(serializedString);
        
        var deserializedResult = await SerializableAggregateListProjector.DeserializeAggregateList<BranchProjector>(
            serializedString!, _domainTypes);
        
        // Assert
        Assert.True(deserializedResult.IsSuccess);
        var restoredProjector = deserializedResult.GetValue();
        
        Assert.Equal(originalProjector.Aggregates.Count, restoredProjector.Aggregates.Count);
        
        var branchAggregate = originalProjector.Aggregates.Values.FirstOrDefault(a => a.GetPayload() is Branch);
        Assert.NotNull(branchAggregate);
        
        var restoredBranchAggregate = restoredProjector.Aggregates[branchAggregate.PartitionKeys];
        Assert.NotNull(restoredBranchAggregate);
        Assert.IsType<Branch>(restoredBranchAggregate.GetPayload());
        
        var emptyAggregate = originalProjector.Aggregates.Values.FirstOrDefault(a => a.GetPayload() is EmptyAggregatePayload);
        Assert.NotNull(emptyAggregate);
        
        var restoredEmptyAggregate = restoredProjector.Aggregates[emptyAggregate.PartitionKeys];
        Assert.NotNull(restoredEmptyAggregate);
        Assert.IsType<EmptyAggregatePayload>(restoredEmptyAggregate.GetPayload());
    }
    
    [Fact]
    public async Task SerializeDeserialize_LargeListProjector_Success()
    {
        var originalProjector = CreateLargeBranchListProjector(100);
        
        // Act
        var serializedJson = await SerializableAggregateListProjector.SerializeAggregateList(originalProjector, _domainTypes);
        var serializedString = serializedJson.IsSuccess ? serializedJson.GetValue() : null;
        Assert.NotNull(serializedString);
        
        var deserializedResult = await SerializableAggregateListProjector.DeserializeAggregateList<BranchProjector>(
            serializedString!, _domainTypes);
        
        // Assert
        Assert.True(deserializedResult.IsSuccess);
        var restoredProjector = deserializedResult.GetValue();
        
        Assert.Equal(100, originalProjector.Aggregates.Count);
        Assert.Equal(100, restoredProjector.Aggregates.Count);
        
        foreach (var partitionKeys in originalProjector.Aggregates.Keys.Take(10))
        {
            Assert.True(restoredProjector.Aggregates.ContainsKey(partitionKeys));
            
            var originalAggregate = originalProjector.Aggregates[partitionKeys];
            var restoredAggregate = restoredProjector.Aggregates[partitionKeys];
            
            if (originalAggregate.GetPayload() is Branch originalBranch && 
                restoredAggregate.GetPayload() is Branch restoredBranch)
            {
                Assert.Equal(originalBranch.Name, restoredBranch.Name);
            }
        }
    }
    
    [Fact]
    public async Task Deserialize_InvalidJson_ReturnsErrorResult()
    {
        // Arrange
        var invalidJson = "{ invalid json ]";
        
        // Act
        var result = await SerializableAggregateListProjector.DeserializeAggregateList<BranchProjector>(
            invalidJson, _domainTypes);
        
        // Assert
        Assert.False(result.IsSuccess);
    }
    
    [Fact]
    public async Task SerializeDeserialize_EmptyListProjector_Success()
    {
        var emptyProjector = new AggregateListProjector<BranchProjector>(ImmutableDictionary<PartitionKeys, Aggregate>.Empty);
        
        // Act
        var serializedJson = await SerializableAggregateListProjector.SerializeAggregateList(emptyProjector, _domainTypes);
        var serializedString = serializedJson.IsSuccess ? serializedJson.GetValue() : null;
        Assert.NotNull(serializedString);
        
        var deserializedResult = await SerializableAggregateListProjector.DeserializeAggregateList<BranchProjector>(
            serializedString!, _domainTypes);
        
        // Assert
        Assert.True(deserializedResult.IsSuccess);
        var restoredProjector = deserializedResult.GetValue();
        
        Assert.Empty(restoredProjector.Aggregates);
    }
    
    [Fact]
    public async Task Deserialize_WrongProjectorType_StillWorks()
    {
        // Arrange
        var originalProjector = CreateTestBranchListProjector();
        
        // Act
        var serializedJson = await SerializableAggregateListProjector.SerializeAggregateList(originalProjector, _domainTypes);
        var serializedString = serializedJson.IsSuccess ? serializedJson.GetValue() : null;
        Assert.NotNull(serializedString);
        
        var deserializedResult = await SerializableAggregateListProjector.DeserializeAggregateList<ClientProjector>(
            serializedString!, _domainTypes);
        
        // Assert
        Assert.True(deserializedResult.IsSuccess);
        var restoredProjector = deserializedResult.GetValue();
        
        Assert.Equal(originalProjector.Aggregates.Count, restoredProjector.Aggregates.Count);
        
        foreach (var partitionKeys in originalProjector.Aggregates.Keys)
        {
            Assert.True(restoredProjector.Aggregates.ContainsKey(partitionKeys));
            
            var originalAggregate = originalProjector.Aggregates[partitionKeys];
            var restoredAggregate = restoredProjector.Aggregates[partitionKeys];
            
            Assert.Equal(originalAggregate.GetPayload().GetType(), restoredAggregate.GetPayload().GetType());
        }
    }
}
