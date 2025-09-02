using Dcb.Domain.Enrollment;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Common;
namespace Dcb.Domain.Student;

/// <summary>
///     複数の学生の在籍サマリを集計するマルチプロジェクタです。
/// </summary>
public record StudentSummaries(Dictionary<Guid, StudentSummaries.Item> Students) : IMultiProjector<StudentSummaries>
{
    public StudentSummaries() : this(new Dictionary<Guid, Item>()) { }

    public static string MultiProjectorVersion => "1.0.0";

    public static ResultBox<StudentSummaries> Project(StudentSummaries payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold)
    {
        var next = new Dictionary<Guid, Item>(payload.Students);
        switch (ev.Payload)
        {
            case StudentCreated created:
                next[created.StudentId] = next.TryGetValue(created.StudentId, out var existingCreated)
                    ? existingCreated with { Name = created.Name }
                    : new Item(created.Name, 0);
                break;
            case StudentEnrolledInClassRoom enrolled:
                if (next.TryGetValue(enrolled.StudentId, out var existingEnrolled))
                {
                    next[enrolled.StudentId] = existingEnrolled with
                    {
                        EnrolledCount = existingEnrolled.EnrolledCount + 1
                    };
                } else
                {
                    next[enrolled.StudentId] = new Item("", 1);
                }
                break;
            case StudentDroppedFromClassRoom dropped:
                if (next.TryGetValue(dropped.StudentId, out var existingDropped))
                {
                    var dec = Math.Max(0, existingDropped.EnrolledCount - 1);
                    next[dropped.StudentId] = existingDropped with { EnrolledCount = dec };
                } else
                {
                    next[dropped.StudentId] = new Item("", 0);
                }
                break;
        }
        return ResultBox.FromValue(new StudentSummaries(next));
    }

    public static StudentSummaries GenerateInitialPayload() => new(new Dictionary<Guid, Item>());

    public static string MultiProjectorName => nameof(StudentSummaries);

    public static string Serialize(DcbDomainTypes domainTypes, string safeWindowThreshold, StudentSummaries safePayload)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold)) throw new ArgumentException("safeWindowThreshold must be supplied", nameof(safeWindowThreshold));
        var dto = new { students = safePayload.Students };
        return System.Text.Json.JsonSerializer.Serialize(dto, domainTypes.JsonSerializerOptions);
    }

    public static StudentSummaries Deserialize(DcbDomainTypes domainTypes, string json)
    {
        var node = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json, domainTypes.JsonSerializerOptions);
        if (node != null && node.TryGetPropertyValue("students", out var s) && s is System.Text.Json.Nodes.JsonObject sobj)
        {
            var dict = new Dictionary<Guid, Item>();
            foreach (var kv in sobj)
            {
                if (Guid.TryParse(kv.Key, out var id) && kv.Value != null)
                {
                    var itemJson = kv.Value!.ToJsonString(domainTypes.JsonSerializerOptions);
                    var item = System.Text.Json.JsonSerializer.Deserialize<Item>(itemJson, domainTypes.JsonSerializerOptions);
                    if (item != null) dict[id] = item;
                }
            }
            return new StudentSummaries(dict);
        }
        return new StudentSummaries(new Dictionary<Guid, Item>());
    }
    /// <summary>
    ///     学生ごとのサマリです。
    /// </summary>
    public record Item(string Name, int EnrolledCount);
}
