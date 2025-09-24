using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Serialization;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Queries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class ListQueryOptionalValueTests
{
    [Fact]
    public async Task ListQuery_WithOptionalDateOnlyItem_ShouldRoundTripThroughGeneralResult()
    {
        var queryTypes = new SimpleQueryTypes();
        queryTypes.RegisterListQuery<TestOptionalDateMultiProjector, OptionalDateListQuery, OptionalDateResult>();

        var services = new ServiceCollection().BuildServiceProvider();
        var projector = new TestOptionalDateMultiProjector(new List<OptionalDateResult>(OptionalDateFixtures.SeedResults));
        var query = new OptionalDateListQuery();

        var generalResult = await queryTypes.ExecuteListQueryAsGeneralAsync(
            query,
            () => Task.FromResult(ResultBox.FromValue<IMultiProjectionPayload>(projector)),
            services,
            safeVersion: null,
            safeWindowThreshold: null,
            safeWindowThresholdTime: null,
            unsafeVersion: null);

        Assert.True(generalResult.IsSuccess);
        var payload = generalResult.GetValue();

        Assert.Equal(OptionalDateFixtures.SeedResults.Count, payload.Items.Count());
        Assert.Contains(nameof(OptionalDateResult), payload.RecordType);

        var typedResult = payload.ToTypedResult<OptionalDateResult>();
        Assert.True(typedResult.IsSuccess);

        var items = typedResult.GetValue().Items.ToList();
        Assert.Equal(OptionalDateFixtures.SeedResults.Count, items.Count);
        Assert.Contains(items, item => item.Date.HasValue && item.Date.GetValue() == OptionalDateFixtures.ExpectedDate);
        Assert.Contains(items, item => !item.Date.HasValue);
    }
}