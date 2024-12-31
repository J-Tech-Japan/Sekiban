using FeatureCheck.Domain.Shared;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosAggregateSubtypeTest(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : AggregateSubtypeTest(
    sekibanTestFixture,
    testOutputHelper,
    new CosmosSekibanServiceProviderGenerator());
