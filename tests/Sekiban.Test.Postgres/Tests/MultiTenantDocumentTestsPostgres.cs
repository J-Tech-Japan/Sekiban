using Sekiban.Infrastructure.Postgres;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Postgres.Tests;

public class MultiTenantDocumentTestsPostgres : MultiTenantDocumentTests
{
    public MultiTenantDocumentTestsPostgres(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new PostgresSekibanServiceProviderGenerator())
    {
    }
}
