using Sekiban.Testing.SingleProjections;
using SekibanEventSourcingBasics.Domain;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Commands;
namespace SekibanEventSourcingBasics.Testing;

public class UserPointSpec : AggregateTest<UserPoint, DomainDependency>
{
    [Fact]
    public void TestCreateUserPoint()
    {
        WhenCommand(new CreateUserPoint("John Doe", "john@example.com",0));
        ThenPayloadIs(new UserPoint("John Doe", "john@example.com", 0));
    }
    [Fact]
    public void TestChangeName()
    {
        GivenCommand(new CreateUserPoint("John Doe", "john@example.com",0));
        WhenCommand(new ChangeUserPointName(GetAggregateId(), "John Smith"));
        ThenPayloadIs(new UserPoint("John Smith", "john@example.com", 0));
    }
    [Fact]
    public void TestCreateUserPointFailedValidation()
    {
        // Empty name
        WhenCommand(new CreateUserPoint("", "john@example.com",0));
        ThenHasValidationErrors();
    }
    [Fact]
    public void TestCreateUserPointFailedValidation2()
    {
        // Wrong email address
        WhenCommand(new CreateUserPoint("John Doe", "john_example.com",0));
        ThenHasValidationErrors();
    }
}
