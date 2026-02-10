using Dcb.Domain;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Runtime.Native;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests.Runtimes;

public class ProjectorRuntimeResolverTests
{
    private readonly DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();

    [Fact]
    public void Resolve_should_return_default_runtime_for_unregistered_projector()
    {
        // Given
        var defaultRuntime = new NativeProjectionRuntime(_domainTypes);
        var resolver = new ProjectorRuntimeResolver(defaultRuntime);

        // When
        var resolved = resolver.Resolve("SomeUnregisteredProjector");

        // Then
        Assert.Same(defaultRuntime, resolved);
    }

    [Fact]
    public void Resolve_should_return_registered_runtime()
    {
        // Given
        var defaultRuntime = new NativeProjectionRuntime(_domainTypes);
        var specialRuntime = new NativeProjectionRuntime(_domainTypes);
        var runtimeMap = new Dictionary<string, IProjectionRuntime>
        {
            ["WeatherForecastProjection"] = specialRuntime
        };
        var resolver = new ProjectorRuntimeResolver(defaultRuntime, runtimeMap);

        // When
        var resolved = resolver.Resolve("WeatherForecastProjection");

        // Then
        Assert.Same(specialRuntime, resolved);
    }

    [Fact]
    public void GetAllRuntimes_should_include_default_and_registered()
    {
        // Given
        var defaultRuntime = new NativeProjectionRuntime(_domainTypes);
        var specialRuntime = new NativeProjectionRuntime(_domainTypes);
        var runtimeMap = new Dictionary<string, IProjectionRuntime>
        {
            ["WeatherForecastProjection"] = specialRuntime
        };
        var resolver = new ProjectorRuntimeResolver(defaultRuntime, runtimeMap);

        // When
        var runtimes = resolver.GetAllRuntimes().ToList();

        // Then
        Assert.Contains(defaultRuntime, runtimes);
        Assert.Contains(specialRuntime, runtimes);
    }

    [Fact]
    public void GetAllRuntimes_should_deduplicate()
    {
        // Given
        var runtime = new NativeProjectionRuntime(_domainTypes);
        var runtimeMap = new Dictionary<string, IProjectionRuntime>
        {
            ["Proj1"] = runtime,
            ["Proj2"] = runtime
        };
        var resolver = new ProjectorRuntimeResolver(runtime, runtimeMap);

        // When
        var runtimes = resolver.GetAllRuntimes().ToList();

        // Then
        Assert.Single(runtimes);
    }

    [Fact]
    public void Constructor_runtimeMap_should_use_last_value_for_duplicate_keys()
    {
        // Given
        var defaultRuntime = new NativeProjectionRuntime(_domainTypes);
        var runtimeB = new NativeProjectionRuntime(_domainTypes);
        var runtimeMap = new Dictionary<string, IProjectionRuntime>
        {
            ["TestProjector"] = runtimeB
        };
        var resolver = new ProjectorRuntimeResolver(defaultRuntime, runtimeMap);

        // When
        var resolved = resolver.Resolve("TestProjector");

        // Then
        Assert.Same(runtimeB, resolved);
    }
}
