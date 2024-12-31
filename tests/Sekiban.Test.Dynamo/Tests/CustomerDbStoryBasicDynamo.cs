using FeatureCheck.Domain.Shared;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class CustomerDbStoryBasicDynamo(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : CustomerDbStoryBasic(
    sekibanTestFixture,
    testOutputHelper,
    new DynamoSekibanServiceProviderGenerator());
