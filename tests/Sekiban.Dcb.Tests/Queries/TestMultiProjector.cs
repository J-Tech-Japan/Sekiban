using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests.Queries;

/// <summary>
///     Test multi-projector for query tests
/// </summary>
public record TestMultiProjector : IMultiProjector<TestMultiProjector>
{
    public List<TestItem> Items { get; init; } = new();
    public int TotalCount { get; init; }

    public static string MultiProjectorName => "TestMultiProjector";
    public static string MultiProjectorVersion => "1.0";

    public static ResultBox<TestMultiProjector> Project(TestMultiProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, TimeProvider timeProvider)
    {
        return ev.Payload switch
        {
            ItemAdded itemAdded => ResultBox.FromValue(
                payload with
                {
                    Items = payload
                        .Items
                        .Append(
                            new TestItem(
                                itemAdded.Id,
                                itemAdded.Name,
                                itemAdded.Category,
                                itemAdded.Price,
                                itemAdded.CreatedAt))
                        .ToList(),
                    TotalCount = payload.TotalCount + 1
                }),
            ItemRemoved itemRemoved => ResultBox.FromValue(
                payload with
                {
                    Items = payload.Items.Where(i => i.Id != itemRemoved.Id).ToList(),
                    TotalCount = Math.Max(0, payload.TotalCount - 1)
                }),
            _ => ResultBox.FromValue(payload)
        };
    }

    public static TestMultiProjector GenerateInitialPayload() => new();
}
