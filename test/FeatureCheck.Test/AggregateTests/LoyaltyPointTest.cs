using ESSampleProjectLib.Exceptions;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.ValueObjects;
using Sekiban.Core.Shared;
using System;
using System.Linq;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class LoyaltyPointTest : AggregateTest<LoyaltyPoint>
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
                    ClientId = clientId, HappenedDate = SekibanDateProducer.GetRegistered().UtcNow, LoyaltyPointValue = new LoyaltyPointValue(100),
                    Note = "test", Reason = new LoyaltyPointReceiveType(LoyaltyPointReceiveTypeKeys.InsuranceUsage),
                    ReferenceVersion = GetCurrentVersion()
                })
            .ThenNotThrowsAnException();
    }
    [Fact]
    public void DeleteClientWillDeleteLoyaltyPointTest()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("Test"));
        var clientId = RunEnvironmentCommand(new CreateClient(branchId, "Test Name", "test@example.com"));
        WhenCommand(new CreateLoyaltyPoint(clientId, 10));
        RunEnvironmentCommandWithPublish(new DeleteClient(clientId));
        var timeStamp = GetLatestEnvironmentEvents().Where(m => m.GetPayload() is LoyaltyPointDeleted).FirstOrDefault()?.TimeStamp;
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
                        ClientId = clientId, HappenedDate = SekibanDateProducer.GetRegistered().UtcNow,
                        LoyaltyPointValue = new LoyaltyPointValue(100),
                        Note = "test", Reason = new LoyaltyPointReceiveType((LoyaltyPointReceiveTypeKeys)10000), // should cause exception
                        ReferenceVersion = GetCurrentVersion()
                    });
            });
    }
}
