using FeatureCheck.Domain.Shared;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Postgres.Tests;

public class CustomerDbStoryBasicPostgres(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : CustomerDbStoryBasic(
    sekibanTestFixture,
    testOutputHelper,
    new PostgresSekibanServiceProviderGenerator());
