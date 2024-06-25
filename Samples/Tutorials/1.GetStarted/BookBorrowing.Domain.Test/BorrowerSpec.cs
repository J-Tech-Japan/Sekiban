using BookBorrowing.Domain.Aggregates.Borrowers;
using BookBorrowing.Domain.Aggregates.Borrowers.Commands;
using BookBorrowing.Domain.ValueObjects;
using Sekiban.Testing.SingleProjections;
namespace BookBorrowing.Domain.Test;

public class BorrowerSpec : AggregateTest<Borrower, BookBorrowingDependency>
{
    [Fact]
    public void CreateTest()
    {
        WhenCommand(
            new CreateBorrower(
                new BorrowerCardNo(100001),
                new Name("John", "Doe"),
                new PhoneNumber("1234567890"),
                new Email("test@example.com")));
        ThenNotThrowsAnException();
        ThenPayloadIs(
            new Borrower(
                new BorrowerCardNo(100001),
                new Name("John", "Doe"),
                new PhoneNumber("1234567890"),
                new Email("test@example.com")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99999)]
    [InlineData(1000000)]
    public void CreateTestValidationFailedBookCard(int cardNumber)
    {
        WhenCommand(
            new CreateBorrower(
                new BorrowerCardNo(cardNumber),
                new Name("John", "Doe"),
                new PhoneNumber("1234567890"),
                new Email("test@example.com")));

        ThenHasValidationErrors();
    }
    [Theory]
    [InlineData("", "test", null)]
    [InlineData("test", "", null)]
    [InlineData("test", "", "aaa")]
    [InlineData("", "test", "aaa")]
    [InlineData("123456789012345678901234567890123456789012345678901", "test", null)]
    [InlineData("test", "123456789012345678901234567890123456789012345678901", null)]
    [InlineData("test", "name", "123456789012345678901234567890123456789012345678901")]
    public void CreateTestValidationFailedName(
        string firstName,
        string lastName,
        string? middleName)
    {
        WhenCommand(
            new CreateBorrower(
                new BorrowerCardNo(100001),
                new Name(firstName, lastName, middleName),
                new PhoneNumber("1234567890"),
                new Email("test@example.com")));

        ThenHasValidationErrors();
    }
    [Theory]
    [InlineData("a.b.c")]
    [InlineData("a.b.c@")]
    [InlineData("@a.b.c")]
    public void CreateTestValidationFailedEmail(string email)
    {
        WhenCommand(
            new CreateBorrower(
                new BorrowerCardNo(100001),
                new Name("John", "Doe"),
                new PhoneNumber("1234567890"),
                new Email(email)));

        ThenHasValidationErrors();
    }

    [Fact]
    public void UpdateNameSpec()
    {
        GivenCommand(
            new CreateBorrower(
                new BorrowerCardNo(100001),
                new Name("John", "Doe"),
                new PhoneNumber("1234567890"),
                new Email("test@example.com")));

        WhenCommand(
            new ChangeBorrowerName(
                GetAggregateId(),
                new Name("John", "Green"),
                "Changed name with married name"));

        ThenNotThrowsAnException();
        ThenPayloadIs(
            new Borrower(
                new BorrowerCardNo(100001),
                new Name("John", "Green"),
                new PhoneNumber("1234567890"),
                new Email("test@example.com")));
    }
}
