using FeatureCheck.Domain.Shared;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;

namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosCommandOrderSpec(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : CommandOrderSpec(
    sekibanTestFixture,
    testOutputHelper,
    new CosmosSekibanServiceProviderGenerator());
