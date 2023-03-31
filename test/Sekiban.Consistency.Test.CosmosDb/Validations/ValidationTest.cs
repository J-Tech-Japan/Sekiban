using Sekiban.Core.Validation;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using Xunit;
namespace SampleProjectStoryXTest.Validations;

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
    [Fact(DisplayName = "検証成功")]
    public void Test1()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 25, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.False(vresults?.Any() ?? false);
        Assert.True(m.TryValidateProperties(out _));
    }
    [Fact]
    public void TestDateOnly()
    {
        var m = new Box
            { Contents = new Contents() };
        var vresults = m.ValidateProperties();
        Assert.False(vresults?.Any() ?? false);
    }
    [Fact]
    public void TestDateOnly2()
    {
        var m = new Contents();
        var vresults = m.ValidateProperties();
        Assert.False(vresults?.Any() ?? false);
    }

    [Fact(DisplayName = "検証失敗_名前未入力")]
    public void Test2()
    {
        var m = new Member { Name = string.Empty, Age = 25, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults?.Any() ?? false);
    }

    [Fact(DisplayName = "検証失敗_名前の桁あふれ")]
    public void Test3()
    {
        var m = new Member
            { Name = "YAMADA Taroooooooooooooooo", Age = 25, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults?.Any() ?? false);
    }

    [Fact(DisplayName = "検証失敗_年齢の数値範囲外")]
    public void Test4()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 80, Tel = "090-1111-2222", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults?.Any() ?? false);
    }

    [Fact(DisplayName = "検証失敗_電話番号の文字種")]
    public void Test5()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 25, Tel = "090-1111-abcd", Email = "hoge@example.com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults?.Any() ?? false);
    }

    [Fact(DisplayName = "検証失敗_メールアドレスの形式")]
    public void Test6()
    {
        var m = new Member { Name = "YAMADA Taro", Age = 25, Tel = "090-1111-2222", Email = "hoge@example@com" };
        var vresults = m.ValidateProperties();
        Assert.True(vresults?.Any() ?? false);
    }

    [Fact(DisplayName = "検証成功_参照型プロパティあり")]
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
        Assert.False(vresults?.Any() ?? false);
    }

    [Fact(DisplayName = "検証失敗_参照型プロパティのプロパティ")]
    public void Test8()
    {
        var m = new Member
        {
            Name = "YAMADA Taro",
            Age = 25,
            Tel = "090-1111-2222",
            Email = "hoge@example.com",
            Partner = new Member
                { Name = "YAMADA Hanakoooooooooooooo", Age = 25, Tel = "080-1111-2222", Email = "hana@example.com" }
        };
        var vresults = m.ValidateProperties();
        Assert.True(vresults?.Any() ?? false);
        Assert.Equal("Partner.Name", vresults?.First()?.MemberNames?.First());
    }

    [Fact(DisplayName = "検証成功_配列型のプロパティあり")]
    public void Test9()
    {
        var m = new Member
        {
            Name = "YAMADA Taro",
            Age = 25,
            Tel = "090-1111-2222",
            Email = "hoge@example.com",
            Partner =
                new Member { Name = "YAMADA Hanako", Age = 25, Tel = "080-1111-2222", Email = "hana@example.com" },
            Friends = new List<Member>
                { new() { Name = "SUZUKI Ichiro", Age = 30 }, new() { Name = "Nakata Hidetoshi", Age = 28 } }
        };
        var vresults = m.ValidateProperties();
        Assert.False(vresults?.Any() ?? false);
    }

    [Fact(DisplayName = "検証失敗_配列型のプロパティに検証に失敗する要素あり")]
    public void Test10()
    {
        var m = new Member
        {
            Name = "YAMADA Taro",
            Age = 25,
            Tel = "090-1111-2222",
            Email = "hoge@example.com",
            Partner =
                new Member { Name = "YAMADA Hanako", Age = 25, Tel = "080-1111-2222", Email = "hana@example.com" },
            Friends = new List<Member>
                { new() { Name = "SUZUKI Ichiro", Age = 30 }, new() { Name = "Nakata Hidetoshi", Age = 90 } }
        };
        var vresults = m.ValidateProperties();
        Assert.True(vresults?.Any() ?? false);
        Assert.Equal("Friends[1].Age", vresults?.First()?.MemberNames?.First());
        Debug.Assert(vresults != null, nameof(vresults) + " != null");
        Assert.Single(vresults);
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
