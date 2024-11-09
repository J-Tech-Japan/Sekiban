using ESSampleProjectLib.Exceptions;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.ValueObjects;
using FeatureCheck.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class LoyaltyPointTest : AggregateTest<LoyaltyPoint, FeatureCheckDependency>
{
    [Fact]
    public void CreateAndAddTest()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("Test"));
        var clientId = RunEnvironmentCommand(new CreateClient(branchId, "Test Name", "test@example.com"));
        WhenCommand(new CreateLoyaltyPointAndAddPoint(clientId, 1000));
        ThenNotThrowsAnException();
        var eventTime = GetLatestEvents()
            .Where(m => m.GetPayload() is LoyaltyPointAdded)
            .Select(m => m.GetPayload() is LoyaltyPointAdded added ? added.HappenedDate : DateTime.Now)
            .First();
        ThenPayloadIs(new LoyaltyPoint(1000, eventTime, false));
    }
    [Fact]
    public void UseValueObjectTest()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("Test"));
        var clientId = RunEnvironmentCommand(new CreateClient(branchId, "Test Name", "test@example.com"));

        WhenCommand(new CreateLoyaltyPoint(clientId, 0))
            .WhenCommand(
                new AddLoyaltyPointWithVO
                {
                    ClientId = clientId,
                    HappenedDate = SekibanDateProducer.GetRegistered().UtcNow,
                    LoyaltyPointValue = new LoyaltyPointValue(100),
                    Note = "test",
                    Reason = new LoyaltyPointReceiveType(LoyaltyPointReceiveTypeKeys.InsuranceUsage),
                    ReferenceVersion = GetCurrentVersion()
                })
            .ThenNotThrowsAnException();
    }
    [Fact]
    public async Task EventOrderTest()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("Test"));
        var clientId = RunEnvironmentCommand(new CreateClient(branchId, "Test Name", "test@example.com"));


        WhenCommand(new CreateLoyaltyPoint(clientId, 10));
        foreach (var i in Enumerable.Range(1, 100))
        {
            WhenCommand(
                new AddLoyaltyPoint(
                    clientId,
                    DateTime.UtcNow,
                    LoyaltyPointReceiveTypeKeys.CreditcardUsage,
                    i * 100,
                    $"test{i}"));

        }
        // check if aggregate order is correct
        var repository = _serviceProvider.GetRequiredService<EventRepository>();
        var eventsOldToNew = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                IDocument.DefaultRootPartitionKey,
                new AggregateTypeStream<LoyaltyPoint>(),
                clientId,
                ISortableIdCondition.None),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(101, eventsOldToNew.Count);


        // check if all event order and max count is correct
        eventsOldToNew = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                IDocument.DefaultRootPartitionKey,
                new AggregateTypeStream<LoyaltyPoint>(),
                clientId,
                ISortableIdCondition.None,
                20),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(20, eventsOldToNew.Count);


        // check if all event order is correct
        eventsOldToNew = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, ISortableIdCondition.None),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(103, eventsOldToNew.Count);

        // check if all event order and max count is correct
        eventsOldToNew = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, ISortableIdCondition.None, 20),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(20, eventsOldToNew.Count);

    }




    [Fact]
    public void DeleteClientWillDeleteLoyaltyPointTest()
    {
        var branchId = GivenEnvironmentCommand(new CreateBranch("Test"));
        var clientId = GivenEnvironmentCommand(new CreateClient(branchId, "Test Name", "test@example.com"));
        WhenCommand(new CreateLoyaltyPoint(clientId, 10));
        RunEnvironmentCommandWithPublish(new DeleteClient(clientId));
        var timeStamp = GetLatestEnvironmentEvents()
            .Where(m => m.GetPayload() is LoyaltyPointDeleted)
            .FirstOrDefault()
            ?.TimeStamp;
        ThenPayloadIs(new LoyaltyPoint(10, timeStamp, true));
    }
    [Fact]
    public void UseValueObjectExceptionTest()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("Test"));
        var clientId = RunEnvironmentCommand(new CreateClient(branchId, "Test Name", "test@example.com"));

        WhenCommand(new CreateLoyaltyPoint(clientId, 0));
        // this will throw an exception before going into WhenCommand, thus you need to use assert.throws
        Assert.Throws<InvalidValueException>(
            () =>
            {
                WhenCommand(
                    new AddLoyaltyPointWithVO
                    {
                        ClientId = clientId,
                        HappenedDate = SekibanDateProducer.GetRegistered().UtcNow,
                        LoyaltyPointValue = new LoyaltyPointValue(100),
                        Note = "test",
                        Reason = new LoyaltyPointReceiveType(
                            (LoyaltyPointReceiveTypeKeys)10000), // should cause exception
                        ReferenceVersion = GetCurrentVersion()
                    });
            });
    }
}
