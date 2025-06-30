using FeatureCheck.Domain.Shared;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;

namespace Sekiban.Test.Postgres.Tests;

public class CommandOrderSpecPostgres(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : CommandOrderSpec(
    sekibanTestFixture,
    testOutputHelper,
    new PostgresSekibanServiceProviderGenerator());
