using System;
using System.Collections.Generic;
using Dcb.Domain.Student;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// タグのパターンマッチ記法に関するサンプルテストです。
/// </summary>
public class TagPatternMatchingTests
{
    /// <summary>
    /// リストパターンを使って StudentTag が含まれるか判定します。
    /// </summary>
    [Fact]
    public void ListPattern_ShouldDetectStudentTag()
    {
        var sid = Guid.NewGuid();
    List<ITag> tags = [ new FallbackTag("Other","X"), new FallbackTag("Foo","Bar"), new StudentTag(sid) ];
    bool hasStudent = tags is [.., StudentTag _];
        Assert.True(hasStudent);
    }

    /// <summary>
    /// List pattern で StudentTag を変数キャプチャし内容を検証します。
    /// </summary>
    [Fact]
    public void ListPattern_ShouldCaptureStudentTag()
    {
        var sid = Guid.NewGuid();
        List<ITag> tags = [ new FallbackTag("Other","X"), new StudentTag(sid) ];
        var content = tags switch
        {
            [.., StudentTag st] => st.GetTagContent(),
            _ => null
        };
        Assert.Equal(sid.ToString(), content);
    }

    /// <summary>
    /// foreach + switch 式パターンで最初の StudentTag を検出します。
    /// </summary>
    [Fact]
    public void ForeachSwitch_ShouldFindStudentTag()
    {
        var sid = Guid.NewGuid();
        List<ITag> tags = [ new FallbackTag("A","1"), new FallbackTag("B","2"), new StudentTag(sid) ];
        Guid? found = null;
        foreach (var tag in tags)
        {
            switch (tag)
            {
                case StudentTag s:
                    found = s.StudentId;
                    goto End;
            }
        }
    End:
        Assert.Equal(sid, found);
    }

    /// <summary>
    /// FirstOrDefault + switch 式で StudentTag のみ処理。
    /// </summary>
    [Fact]
    public void FirstOrDefaultSwitch_ShouldProcessStudentTag()
    {
        var sid = Guid.NewGuid();
        List<ITag> tags = [ new FallbackTag("Other","X"), new StudentTag(sid) ];
        var firstStudentMessage = tags.Find(t => t is StudentTag) switch
        {
            StudentTag s => $"Student:{s.StudentId}",
            _ => "None"
        };
        Assert.Equal($"Student:{sid}", firstStudentMessage);
    }

    /// <summary>
    /// StudentTag が先頭/中央/末尾いずれでも検出できることを確認します。
    /// </summary>
    [Fact]
    public void AnyPosition_ShouldDetectStudentTagRegardlessOfOrder()
    {
        var sid = Guid.NewGuid();
        List<ITag> tagsMiddle = [ new FallbackTag("A","1"), new StudentTag(sid), new FallbackTag("B","2") ];
        List<ITag> tagsFirst  = [ new StudentTag(sid), new FallbackTag("A","1"), new FallbackTag("B","2") ];
        List<ITag> tagsLast   = [ new FallbackTag("A","1"), new FallbackTag("B","2"), new StudentTag(sid) ];

        bool HasStudent(List<ITag> ts) => ts.Exists(t => t is StudentTag);

        Assert.True(HasStudent(tagsMiddle));
        Assert.True(HasStudent(tagsFirst));
        Assert.True(HasStudent(tagsLast));
    }

    /// <summary>
    /// スライスパターン [..] を用いて StudentTag が 先頭/中央/末尾 にあるケースを判定します。
    /// </summary>
    [Fact]
    public void SlicePatterns_ShouldDetectFirstMiddleLastPositions()
    {
        var sid = Guid.NewGuid();
        List<ITag> tagsFirst  = [ new StudentTag(sid), new FallbackTag("X","1"), new FallbackTag("Y","2") ];
        List<ITag> tagsMiddle = [ new FallbackTag("X","1"), new StudentTag(sid), new FallbackTag("Y","2") ];
        List<ITag> tagsLast   = [ new FallbackTag("X","1"), new FallbackTag("Y","2"), new StudentTag(sid) ];

        // 先頭: [ StudentTag _, .. ]
        Assert.True(tagsFirst is [StudentTag _, ..]);
        Assert.False(tagsFirst is [.., StudentTag _]);

        // 末尾: [ .., StudentTag _ ]
        Assert.True(tagsLast is [.., StudentTag _]);
        Assert.False(tagsLast is [StudentTag _, ..]);

        // 中央: 先頭要素1件, 中央に StudentTag, 最後に1件, 中間はスライスで可変
        // パターン: [ _, .., StudentTag _, _ ]  (スライスは 1 回のみ使用)
        Assert.True(tagsMiddle is [_, .., StudentTag _, _]);
        // ネガティブ: 中央リストは先頭でも末尾でもない
        Assert.False(tagsMiddle is [StudentTag _, ..]);
        Assert.False(tagsMiddle is [.., StudentTag _]);
    }

    /// <summary>
    /// 拡張メソッド HasTag / TryGetTag とパターンマッチの組み合わせを検証します。
    /// </summary>
    [Fact]
    public void ExtensionMethods_ShouldWorkWithPatternMatching()
    {
        var sid = Guid.NewGuid();
        List<ITag> tags = [ new FallbackTag("Other","X"), new StudentTag(sid) ];

        Assert.True(tags.HasTag<StudentTag>());
        Assert.False(tags.HasTag<FallbackTag>() is false); // 常に true になる式の否定チェック(意図的形式)

        Assert.True(tags.TryGetTag<StudentTag>(out var student));
        Assert.NotNull(student);

        // pattern matching switch で型分岐
        string label = student switch
        {
            StudentTag st => $"Student:{st.StudentId}",
            _ => "Unknown"
        };
        Assert.Equal($"Student:{sid}", label);
    }

    /// <summary>
    /// GetTagGroups で複数 StudentTag が取得できることを確認します。
    /// </summary>
    [Fact]
    public void ExtensionMethods_ShouldEnumerateAllStudentTags()
    {
        var sid1 = Guid.NewGuid();
        var sid2 = Guid.NewGuid();
        List<ITag> tags = [ new StudentTag(sid1), new FallbackTag("Other","X"), new StudentTag(sid2) ];
    var students = tags.GetTagGroups<StudentTag>();
        Assert.Contains(students, s => s.StudentId == sid1);
        Assert.Contains(students, s => s.StudentId == sid2);
    }
}
