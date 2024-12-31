using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Postgres.Tests;

public class MultipleEventsInOneClassTestPostgres : MultipleEventsInOneClassTest
{
    public MultipleEventsInOneClassTestPostgres(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new PostgresSekibanServiceProviderGenerator())
    {
    }
}
