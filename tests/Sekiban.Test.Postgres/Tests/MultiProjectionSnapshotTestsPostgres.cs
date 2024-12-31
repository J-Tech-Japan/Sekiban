using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Postgres.Tests;

public class MultiProjectionSnapshotTestsPostgres : MultiProjectionSnapshotTests
{
    public MultiProjectionSnapshotTestsPostgres(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new PostgresSekibanServiceProviderGenerator())
    {
    }
}
