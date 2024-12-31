using FeatureCheck.Domain.Shared;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosInheritInSubtypesTest(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper output) : InheritInSubtypesTest(
    sekibanTestFixture,
    output,
    new CosmosSekibanServiceProviderGenerator())
{
}
