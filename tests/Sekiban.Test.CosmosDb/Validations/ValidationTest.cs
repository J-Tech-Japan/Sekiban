using Sekiban.Core.Validation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
namespace Sekiban.Test.CosmosDb.Validations;

public class ValidationTest
{
    [Fact]
    public void Test1()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 25, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.False(vresults.Any());
        Assert.True(m.TryValidateProperties(out _));
    }
    [Fact]
    public void TestDateOnly()
    {
        var m = new Box { Contents = new Contents() };
        var vresults = m.ValidateProperties();
        Assert.False(vresults.Any());
    }
    [Fact]
    public void TestDateOnly2()
    {
        var m = new Contents();
        var vresults = m.ValidateProperties();
        Assert.False(vresults.Any());
    }

    [Fact]
    public void VerificationReturnsValidationErrorNameNotEntered()
    {
        var m = new Member { Name = string.Empty, Age = 25, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestNameDigitOverflow()
    {
        var m = new Member { Name = "YAMADA Taroooooooooooooooo", Age = 25, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestAgeOutsideNumericRange()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 80, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestCharacterTypeOfPhoneNumber()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 25, Tel = "090-1111-abcd", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestEmailAddressFormat()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 25, Tel = "090-1111-2222", Email = "hoge@example@com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestReferenceTypePropertyExists()
    {
        var m = new Member
        {
            Name = "YAMADA Taro",
            Age = 25,
            Tel = "090-1111-2222",
            Email = "hoge@example.com",
            Partner = new Member { Name = "YAMADA Hanako", Age = 25, Tel = "080-1111-2222", Email = "hana@example.com" }
        };
        var vresults = m.ValidateProperties();
        Assert.False(vresults.Any());
    }

    [Fact]
    public void TestPropertyOfReferenceTypeProperty()
    {
        var m = new Member
        {
            Name = "YAMADA Taro",
            Age = 25,
            Tel = "090-1111-2222",
            Email = "hoge@example.com",
            Partner = new Member { Name = "YAMADA Hanakoooooooooooooo", Age = 25, Tel = "080-1111-2222", Email = "hana@example.com" }
        };
        var vresults = m.ValidateProperties();
        var validationResults = vresults.ToList();
        Assert.True(validationResults.Count != 0);
        Assert.Equal("Partner.Name", validationResults.First().MemberNames.First());
    }

    [Fact]
    public void TestArrayTypePropertyExists()
    {
        var m = new Member
        {
            Name = "YAMADA Taro",
            Age = 25,
            Tel = "090-1111-2222",
            Email = "hoge@example.com",
            Partner = new Member { Name = "YAMADA Hanako", Age = 25, Tel = "080-1111-2222", Email = "hana@example.com" },
            Friends = [new() { Name = "SUZUKI Ichiro", Age = 30 }, new() { Name = "Nakata Hidetoshi", Age = 28 }]
        };
        var vresults = m.ValidateProperties();
        Assert.False(vresults.Any());
    }

    [Fact]
    public void TestFailsValidationInArrayType()
    {
        var m = new Member
        {
            Name = "YAMADA Taro",
            Age = 25,
            Tel = "090-1111-2222",
            Email = "hoge@example.com",
            Partner = new Member { Name = "YAMADA Hanako", Age = 25, Tel = "080-1111-2222", Email = "hana@example.com" },
            Friends = [new() { Name = "SUZUKI Ichiro", Age = 30 }, new() { Name = "Nakata Hidetoshi", Age = 90 }]
        };
        var vresults = m.ValidateProperties();
        var validationResults = vresults.ToList();
        Assert.True(validationResults.Count != 0);
        Assert.Equal("Friends[1].Age", validationResults.First().MemberNames.First());
        Debug.Assert(vresults != null, nameof(vresults) + " != null");
        Assert.Single(validationResults);
    }

    public record Contents
    {
        public DateOnly Date { get; init; } = default;
        public DateTime DateTime { get; init; } = default;
    }

    public record Box
    {
        public Contents Contents { get; init; } = default!;
    }
}
