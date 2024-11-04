using FeatureCheck.Domain.Shared;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class CustomerDbStoryBasicDynamo(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : CustomerDbStoryBasic(
    sekibanTestFixture,
    testOutputHelper,
    new DynamoSekibanServiceProviderGenerator());
public class EventOrderSpecDynamo(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : EventOrderSpec(
    sekibanTestFixture,
    testOutputHelper,
    new DynamoSekibanServiceProviderGenerator());
