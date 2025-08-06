using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

[Collection("PostgresTests")]
public abstract class PostgresTestBase : IAsyncLifetime
{
    protected PostgresTestFixture Fixture { get; }
    
    protected PostgresTestBase(PostgresTestFixture fixture)
    {
        Fixture = fixture;
    }
    
    public virtual Task InitializeAsync()
    {
        // Clear database before each test
        return Fixture.ClearDatabaseAsync();
    }
    
    public virtual Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}

// Collection definition for shared fixture
[CollectionDefinition("PostgresTests")]
public class PostgresTestCollection : ICollectionFixture<PostgresTestFixture>
{
}