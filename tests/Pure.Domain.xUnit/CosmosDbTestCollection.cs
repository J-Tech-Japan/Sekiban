namespace Pure.Domain.xUnit;

/// <summary>
///     Test collection definition to ensure CosmosDB tests run sequentially
/// </summary>
[CollectionDefinition("CosmosDbTests")]
public class CosmosDbTestCollection : ICollectionFixture<CosmosDbTestCollectionFixture>
{
}
