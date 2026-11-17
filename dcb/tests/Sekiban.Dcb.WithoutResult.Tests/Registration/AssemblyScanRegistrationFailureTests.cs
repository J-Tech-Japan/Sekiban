using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WithoutResult.Tests.Registration.ScanFailure.Custom;
using Sekiban.Dcb.WithoutResult.Tests.Registration.ScanFailure.Plain;
namespace Sekiban.Dcb.WithoutResult.Tests.Registration
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
                        typeof(PlainFaultyNameProjector).Assembly,
                        typeof(PlainFaultyNameProjector).Namespace!)));

            Assert.Equal(nameof(PlainFaultyNameProjector), thrown.Message);
        }

        [Fact]
        public void ScannedCustomSerializedProjector_WhoseNameGetterThrows_SurfacesThatException()
        {
            var thrown = Assert.Throws<ProjectorNameUnavailableException>(() =>
                DcbDomainTypesExtensions.Simple(builder =>
                    builder.AddMultiProjectorsFromNamespace(
                        typeof(CustomFaultyNameProjector).Assembly,
                        typeof(CustomFaultyNameProjector).Namespace!)));

            Assert.Equal(nameof(CustomFaultyNameProjector), thrown.Message);
        }

        [Fact]
        public void DirectCustomSerializedRegistration_WhoseNameGetterThrows_SurfacesThatException()
        {
            var types = new SimpleMultiProjectorTypes();

            var thrown = Assert.Throws<ProjectorNameUnavailableException>(
                () => types.RegisterProjectorWithCustomSerialization<CustomFaultyNameProjector>());

            Assert.Equal(nameof(CustomFaultyNameProjector), thrown.Message);
        }
    }

    public sealed class ProjectorNameUnavailableException(string message) : Exception(message);
}

namespace Sekiban.Dcb.WithoutResult.Tests.Registration.ScanFailure.Plain
{
    public record PlainFaultyNameProjector : IMultiProjector<PlainFaultyNameProjector>
    {
        public static string MultiProjectorName => throw new ProjectorNameUnavailableException(nameof(PlainFaultyNameProjector));
        public static string MultiProjectorVersion => "1.0";

        public static PlainFaultyNameProjector Project(
            PlainFaultyNameProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => payload;

        public static PlainFaultyNameProjector GenerateInitialPayload() => new();
    }
}

namespace Sekiban.Dcb.WithoutResult.Tests.Registration.ScanFailure.Custom
{
    public record CustomFaultyNameProjector : IMultiProjectorWithCustomSerialization<CustomFaultyNameProjector>
    {
        public static string MultiProjectorName => throw new ProjectorNameUnavailableException(nameof(CustomFaultyNameProjector));
        public static string MultiProjectorVersion => "1.0";

        public static CustomFaultyNameProjector Project(
            CustomFaultyNameProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => payload;

        public static CustomFaultyNameProjector GenerateInitialPayload() => new();

        public static SerializationResult Serialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            CustomFaultyNameProjector payload) => throw new NotSupportedException();

        public static CustomFaultyNameProjector Deserialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            ReadOnlySpan<byte> data) => throw new NotSupportedException();
    }
}
