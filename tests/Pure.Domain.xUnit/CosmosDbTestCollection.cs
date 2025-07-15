namespace Pure.Domain.xUnit;

/// <summary>
/// Test collection definition to ensure CosmosDB tests run sequentially
/// </summary>
[CollectionDefinition("CosmosDbTests")]
public class CosmosDbTestCollection : ICollectionFixture<CosmosDbTestCollectionFixture>
{
}

/// <summary>
/// Collection fixture for CosmosDB tests
/// </summary>
public class CosmosDbTestCollectionFixture
{
    public CosmosDbTestCollectionFixture()
    {
        // Initialize any shared resources if needed
    }
}
