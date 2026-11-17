using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WithResult.Tests.Registration.ScanFailure;
namespace Sekiban.Dcb.WithResult.Tests.Registration
{
    /// <summary>
    ///     Registration through the assembly scan is reflected. A failure inside a registration must reach the caller
    ///     as the exception the registration threw, not wrapped in a <see cref="System.Reflection.TargetInvocationException" />.
    /// </summary>
    public class AssemblyScanRegistrationFailureTests
    {
        [Fact]
        public void ScannedProjector_WhoseNameGetterThrows_SurfacesThatException()
        {
            var thrown = Assert.Throws<ProjectorNameUnavailableException>(() =>
                DcbDomainTypesExtensions.Simple(builder =>
                    builder.AddMultiProjectorsFromNamespace(
                        typeof(FaultyNameProjector).Assembly,
                        typeof(FaultyNameProjector).Namespace!)));

            Assert.Equal(nameof(FaultyNameProjector), thrown.Message);
        }
    }

    public sealed class ProjectorNameUnavailableException(string message) : Exception(message);
}

namespace Sekiban.Dcb.WithResult.Tests.Registration.ScanFailure
{
    public record FaultyNameProjector : IMultiProjector<FaultyNameProjector>
    {
        public static string MultiProjectorName => throw new ProjectorNameUnavailableException(nameof(FaultyNameProjector));
        public static string MultiProjectorVersion => "1.0";

        public static ResultBox<FaultyNameProjector> Project(
            FaultyNameProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);

        public static FaultyNameProjector GenerateInitialPayload() => new();
    }
}
