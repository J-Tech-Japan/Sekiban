using FeatureCheck.Domain.Shared;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;

namespace Sekiban.Test.Dynamo.Tests;

public class CommandOrderSpecDynamo(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : CommandOrderSpec(
    sekibanTestFixture,
    testOutputHelper,
    new DynamoSekibanServiceProviderGenerator());
