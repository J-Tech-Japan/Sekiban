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
    ///     Registration through the assembly scan is reflected. A failure inside a registration reaches the caller as
    ///     the exception the registration threw, not wrapped in a <see cref="System.Reflection.TargetInvocationException" />
    ///     — except a custom-serialized projector's scanned registration, whose failure the scan skips as 10.x did.
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

        /// <summary>
        ///     10.x behaviour, kept: the scan discarded a custom-serialized projector's failed registration and went on,
        ///     so the other projectors of the same scan are still registered.
        /// </summary>
        [Fact]
        public void ScannedCustomSerializedProjector_WhoseNameGetterThrows_IsSkipped_AndTheScanContinues()
        {
            var registered = -1;
            var domainTypes = DcbDomainTypesExtensions.Simple(builder =>
                registered = builder.AddMultiProjectorsFromNamespace(
                    typeof(CustomFaultyNameProjector).Assembly,
                    typeof(CustomFaultyNameProjector).Namespace!));

            // The count is the candidates the scan found, as in 10.x.
            Assert.Equal(3, registered);
            var names = domainTypes.MultiProjectorTypes.GetAllProjectorNames();
            Assert.Equal(
                new[] { HealthyCustomProjector.MultiProjectorName, HealthyPlainProjector.MultiProjectorName }.Order(),
                names.Order());
            Assert.Equal("2.0", domainTypes.MultiProjectorTypes.GetProjectorVersion(HealthyCustomProjector.MultiProjectorName));
            Assert.Equal("3.0", domainTypes.MultiProjectorTypes.GetProjectorVersion(HealthyPlainProjector.MultiProjectorName));
        }

        /// <summary>
        ///     Only the registration's own failure is skipped. A scanned type that matches the custom-serialization
        ///     filter but breaks the self-type constraint fails to resolve the generic registration, and that
        ///     ArgumentException escapes the scan, as in 10.x.
        /// </summary>
        [Fact]
        public void ScannedCustomSerializedProjector_BreakingTheSelfTypeConstraint_FailsTheScan()
        {
            Assert.Throws<ArgumentException>(() =>
                DcbDomainTypesExtensions.Simple(builder =>
                    builder.AddMultiProjectorsFromNamespace(
                        typeof(ScanFailure.Constraint.DerivedCustomProjector).Assembly,
                        typeof(ScanFailure.Constraint.DerivedCustomProjector).Namespace!)));
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

    public record HealthyCustomProjector : IMultiProjectorWithCustomSerialization<HealthyCustomProjector>
    {
        public static string MultiProjectorName => "scan-failure-healthy-custom";
        public static string MultiProjectorVersion => "2.0";

        public static HealthyCustomProjector Project(
            HealthyCustomProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => payload;

        public static HealthyCustomProjector GenerateInitialPayload() => new();

        public static SerializationResult Serialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            HealthyCustomProjector payload) => throw new NotSupportedException();

        public static HealthyCustomProjector Deserialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            ReadOnlySpan<byte> data) => throw new NotSupportedException();
    }

    public record HealthyPlainProjector : IMultiProjector<HealthyPlainProjector>
    {
        public static string MultiProjectorName => "scan-failure-healthy-plain";
        public static string MultiProjectorVersion => "3.0";

        public static HealthyPlainProjector Project(
            HealthyPlainProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => payload;

        public static HealthyPlainProjector GenerateInitialPayload() => new();
    }
}

namespace Sekiban.Dcb.WithoutResult.Tests.Registration.ScanFailure.Constraint
{
    public record BaseCustomProjector : IMultiProjectorWithCustomSerialization<BaseCustomProjector>
    {
        public static string MultiProjectorName => "scan-failure-constraint-base";
        public static string MultiProjectorVersion => "1.0";

        public static BaseCustomProjector Project(
            BaseCustomProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => payload;

        public static BaseCustomProjector GenerateInitialPayload() => new();

        public static SerializationResult Serialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            BaseCustomProjector payload) => throw new NotSupportedException();

        public static BaseCustomProjector Deserialize(
            DcbDomainTypes domainTypes,
            string safeWindowThreshold,
            ReadOnlySpan<byte> data) => throw new NotSupportedException();
    }

    /// <summary>Implements the interface only through its base, so it is not its own TSelf.</summary>
    public record DerivedCustomProjector : BaseCustomProjector;
}
