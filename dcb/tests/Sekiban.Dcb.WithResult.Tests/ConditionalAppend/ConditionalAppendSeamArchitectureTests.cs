using System.Reflection;
using System.Runtime.CompilerServices;
using Dcb.Domain;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     Structural guard for the SEK-G16 post-commit response-loss TEST seam (<c>AfterConditionalCommitHook</c>). It proves
///     the seam cannot silently become a public/production-reachable runtime mutation surface: it is a NON-public INSTANCE
///     member (never static/global, so parallel instances cannot leak state), absent from the public event-store
///     interfaces, and left null by normal construction (production composition never assigns it).
/// </summary>
public class ConditionalAppendSeamArchitectureTests
{
    private const string HookName = "AfterConditionalCommitHook";

    public static IEnumerable<object[]> SeamStoreTypes => new[]
    {
        new object[] { typeof(SqliteEventStore) },
        new object[] { typeof(CosmosDbEventStore) }
    };

    [Theory]
    [MemberData(nameof(SeamStoreTypes))]
    public void Hook_IsNonPublic_Instance_NotStatic(Type storeType)
    {
        var nonPublic = storeType.GetProperty(HookName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(nonPublic);                                                    // exists as an internal instance member
        Assert.Null(storeType.GetProperty(HookName, BindingFlags.Instance | BindingFlags.Public)); // not public
        Assert.Null(storeType.GetProperty(HookName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)); // never static/global
    }

    [Fact]
    public void Hook_IsAbsentFromPublicEventStoreInterfaces()
    {
        foreach (var contract in new[] { typeof(IEventStore), typeof(IConditionalEventStore), typeof(IHotEventStore) })
        {
            Assert.Null(contract.GetProperty(HookName));
        }
    }

    [Fact]
    public void Hook_StateIsPerInstance_NoStaticLeak()
    {
        var domain = DomainType.GetDomainTypes();
        var a = new SqliteEventStore(Path.Combine(Path.GetTempPath(), $"seam-a-{Guid.NewGuid():N}.db"), domain.EventTypes);
        var b = new SqliteEventStore(Path.Combine(Path.GetTempPath(), $"seam-b-{Guid.NewGuid():N}.db"), domain.EventTypes);

        Assert.Null(a.AfterConditionalCommitHook);   // default: unset
        Assert.Null(b.AfterConditionalCommitHook);
        a.AfterConditionalCommitHook = () => Task.CompletedTask;
        Assert.NotNull(a.AfterConditionalCommitHook);
        Assert.Null(b.AfterConditionalCommitHook);   // setting one instance never leaks to another
    }

    [Fact]
    public void ProductionComposition_LeavesHookUnset_Sqlite()
    {
        var domain = DomainType.GetDomainTypes();
        // A store built the way production builds it carries no hook.
        var store = new SqliteEventStore(
            Path.Combine(Path.GetTempPath(), $"seam-prod-{Guid.NewGuid():N}.db"),
            domain.EventTypes, null, null, new DefaultServiceIdProvider());
        Assert.Null(store.AfterConditionalCommitHook);
    }

    [Fact]
    public void ProductionComposition_LeavesHookUnset_Cosmos()
    {
        var domain = DomainType.GetDomainTypes();
        var options = new CosmosDbEventStoreOptions { EventsContainerName = "events", TagsContainerName = "tags" };
        var context = new CosmosDbContext(new InMemoryCosmosClient(), "db", null, options);
        var store = new CosmosDbEventStore(
            context, domain.EventTypes, new DefaultServiceIdProvider(), new DefaultCosmosContainerResolver(options));
        Assert.Null(store.AfterConditionalCommitHook);
    }

    [Fact]
    public void InternalsVisibleTo_Allowlist_IsExactlyTheIntendedAssemblies()
    {
        // The provider assemblies grant internals only to the intended test assembly; Core grants ONLY the four provider
        // assemblies (for the internal post-commit-response-loss signal) — never a test assembly.
        AssertIvtAllowlist(typeof(SqliteEventStore).Assembly, "Sekiban.Dcb.WithResult.Tests");
        AssertIvtAllowlist(typeof(CosmosDbEventStore).Assembly, "Sekiban.Dcb.WithResult.Tests");
        AssertIvtAllowlist(
            typeof(IConditionalEventStore).Assembly,
            "Sekiban.Dcb.Postgres", "Sekiban.Dcb.Sqlite", "Sekiban.Dcb.CosmosDb", "Sekiban.Dcb.DynamoDB");
    }

    [Theory]
    [MemberData(nameof(SeamStoreTypes))]
    public void Hook_IsNotConstructorInjected_NorAnOptionOrDiSurface(Type storeType)
    {
        // Not a constructor parameter (so DI/composition cannot supply it).
        foreach (var ctor in storeType.GetConstructors())
        {
            Assert.DoesNotContain(ctor.GetParameters(), p =>
                p.Name == "afterConditionalCommitHook" || p.ParameterType == typeof(Func<Task>));
        }

        // Not a public settable option/DI surface: no PUBLIC Func<Task> property anywhere on the store.
        Assert.DoesNotContain(
            storeType.GetProperties(BindingFlags.Instance | BindingFlags.Public),
            p => p.PropertyType == typeof(Func<Task>));
    }

    [Fact]
    public void OptionsTypes_DoNotExposeTheHook()
    {
        Assert.DoesNotContain(
            typeof(CosmosDbEventStoreOptions).GetProperties(), p => p.PropertyType == typeof(Func<Task>));
        Assert.DoesNotContain(
            typeof(SqliteEventStoreOptions).GetProperties(), p => p.PropertyType == typeof(Func<Task>));
    }

    [Fact]
    public void ProductionAssemblies_ContainNoAssignmentCallSiteForTheSeams()
    {
        // IL-level guard: no production method body calls the response-loss hook setter or the allocator/budget probe
        // setters. The `= default` initializers compile to a backing-field store (stfld) in the declaring ctor, NOT a
        // `call set_X`, so any `x.Seam = ...` assignment — the reachable mutation surface we forbid — would be caught here.
        AssertNoSetterCallSites(typeof(SqliteEventStore).Assembly, "set_AfterConditionalCommitHook");
        AssertNoSetterCallSites(typeof(CosmosDbEventStore).Assembly, "set_AfterConditionalCommitHook");
        AssertNoSetterCallSites(
            typeof(IConditionalEventStore).Assembly,
            "set_ConditionalEventIdFactory", "set_ConditionalSortableIdFactory", "set_VerificationBudgetOverride");
    }

    private static void AssertNoSetterCallSites(Assembly assembly, params string[] setterNames)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        var targets = new HashSet<string>(setterNames, StringComparer.Ordinal);
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var type in types)
        {
            var methods = type.GetMethods(all | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                .Concat(type.GetConstructors(all | BindingFlags.DeclaredOnly));
            foreach (var method in methods)
            {
                byte[]? il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch { il = null; }
                if (il is null)
                {
                    continue;
                }

                // Per-byte scan: at every offset where a call (0x28) or callvirt (0x6F) opcode could sit, resolve the next
                // 4 bytes as a metadata token. Checking every offset never MISSES a real call; a coincidental
                // (non-call) byte resolving to a method whose Name is exactly one of the very specific seam setters is
                // effectively impossible, so this only fires on a genuine assignment.
                for (var i = 0; i + 5 <= il.Length; i++)
                {
                    if (il[i] != 0x28 && il[i] != 0x6F)
                    {
                        continue;
                    }

                    MethodBase? callee;
                    try
                    {
                        callee = method.Module.ResolveMethod(
                            BitConverter.ToInt32(il, i + 1), type.GetGenericArguments(), method.GetGenericArguments());
                    }
                    catch { continue; }

                    if (callee is not null && targets.Contains(callee.Name))
                    {
                        Assert.Fail(
                            $"Production assembly '{assembly.GetName().Name}' assigns seam '{callee.Name}' in "
                            + $"{type.FullName}.{method.Name}.");
                    }
                }
            }
        }
    }

    private static void AssertIvtAllowlist(Assembly assembly, params string[] expected)
    {
        var actual = assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName.Split(',')[0].Trim())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), actual);
    }
}
