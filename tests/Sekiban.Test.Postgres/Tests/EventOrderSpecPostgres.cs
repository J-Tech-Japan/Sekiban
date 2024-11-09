using FeatureCheck.Domain.Shared;
using Sekiban.Infrastructure.Postgres;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Postgres.Tests;

public class EventOrderSpecPostgres(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : EventOrderSpec(
    sekibanTestFixture,
    testOutputHelper,
    new PostgresSekibanServiceProviderGenerator());
