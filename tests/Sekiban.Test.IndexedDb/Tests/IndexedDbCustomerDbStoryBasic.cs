using FeatureCheck.Domain.Shared;
using Sekiban.Infrastructure.IndexedDb;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.IndexedDb.Tests;

public class IndexedDbCustomerDbStoryBasic(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper) : CustomerDbStoryBasic(
    sekibanTestFixture,
    testOutputHelper,
    new IndexedDbSekibanServiceProviderGenerator());
