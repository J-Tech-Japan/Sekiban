using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Tests;

[
    GenerateSerializer
]
public record TestOptionalDateMultiProjector([property: Id(0)] List<OptionalDateResult> Results)
    : IMultiProjector<TestOptionalDateMultiProjector>
{
    public TestOptionalDateMultiProjector() : this(new List<OptionalDateResult>()) { }

    public static string MultiProjectorName => "OptionalDateProjector";
    public static string MultiProjectorVersion => "1.0";

    public static TestOptionalDateMultiProjector GenerateInitialPayload() =>
        new(new List<OptionalDateResult>(OptionalDateFixtures.SeedResults));

    public static ResultBox<TestOptionalDateMultiProjector> Project(
        TestOptionalDateMultiProjector payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
}