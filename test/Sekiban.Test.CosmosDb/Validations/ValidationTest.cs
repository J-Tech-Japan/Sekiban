using Sekiban.Core.Validation;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using Xunit;
namespace Sekiban.Test.CosmosDb.Validations;

public class Member
{
    [Required]
    [MaxLength(20)]
    public string Name { get; init; } = default!;

    [Required]
    [Range(18, 75)]
    public int Age { get; init; }

    public int? No { get; init; }

    [Phone]
    [MaxLength(15)]
    [MinLength(10)]
    public string? Tel { get; init; }

    [EmailAddress]
    [MaxLength(254)]
    [MinLength(8)]
    public string? Email { get; init; }

    public Member? Partner { get; init; }

    public List<Member> Friends { get; init; } = new();
}
public class ValidationTest
{
    [Fact(DisplayName = "Verification successful.")]
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

    [Fact(DisplayName = "Verification failed - Name not entered.")]
    public void Test2()
    {
        var m = new Member { Name = string.Empty, Age = 25, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact(DisplayName = "Verification failed - Name digit overflow.")]
    public void Test3()
    {
        var m = new Member { Name = "YAMADA Taroooooooooooooooo", Age = 25, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact(DisplayName = "Verification failed - Age outside numeric range.")]
    public void Test4()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 80, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact(DisplayName = "Verification failed - Character type of phone number.")]
    public void Test5()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 25, Tel = "090-1111-abcd", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact(DisplayName = "Verification failed - Email address format.")]
    public void Test6()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 25, Tel = "090-1111-2222", Email = "hoge@example@com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact(DisplayName = "Verification successful - Reference type property exists.")]
    public void Test7()
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

    [Fact(DisplayName = "Verification failed - Property of reference-type property.")]
    public void Test8()
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
        Assert.True(validationResults.Any());
        Assert.Equal("Partner.Name", validationResults.First().MemberNames.First());
    }

    [Fact(DisplayName = "Verification successful - Array type property exists.")]
    public void Test9()
    {
        var m = new Member
        {
            Name = "YAMADA Taro",
            Age = 25,
            Tel = "090-1111-2222",
            Email = "hoge@example.com",
            Partner = new Member { Name = "YAMADA Hanako", Age = 25, Tel = "080-1111-2222", Email = "hana@example.com" },
            Friends = new List<Member> { new() { Name = "SUZUKI Ichiro", Age = 30 }, new() { Name = "Nakata Hidetoshi", Age = 28 } }
        };
        var vresults = m.ValidateProperties();
        Assert.False(vresults.Any());
    }

    [Fact(DisplayName = "Verification failed - There is an element that fails validation in array type property.")]
    public void Test10()
    {
        var m = new Member
        {
            Name = "YAMADA Taro",
            Age = 25,
            Tel = "090-1111-2222",
            Email = "hoge@example.com",
            Partner = new Member { Name = "YAMADA Hanako", Age = 25, Tel = "080-1111-2222", Email = "hana@example.com" },
            Friends = new List<Member> { new() { Name = "SUZUKI Ichiro", Age = 30 }, new() { Name = "Nakata Hidetoshi", Age = 90 } }
        };
        var vresults = m.ValidateProperties();
        var validationResults = vresults.ToList();
        Assert.True(validationResults.Any());
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
