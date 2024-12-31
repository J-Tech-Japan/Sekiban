using FeatureCheck.Domain.Shared;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class EventOrderSpecDynamo(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : EventOrderSpec(
    sekibanTestFixture,
    testOutputHelper,
    new DynamoSekibanServiceProviderGenerator());
