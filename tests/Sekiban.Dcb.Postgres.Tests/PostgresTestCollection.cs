using Xunit;
namespace Sekiban.Dcb.Postgres.Tests;

[CollectionDefinition("PostgresTests")]
public class PostgresTestCollection : ICollectionFixture<PostgresTestFixture>
{
}