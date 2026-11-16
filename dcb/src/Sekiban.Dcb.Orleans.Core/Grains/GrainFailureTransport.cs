using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Prepares a failure that a grain contract reports by faulting its task for the trip to the caller. Orleans
///     serializes the exception itself, so its type and message survive the hop (and, for the Sekiban exception types
///     with a registered converter, the properties callers read). Orleans cannot carry every exception: a type it has no
///     codec for makes the call fail with a codec error that drops the message, and the exception codec does not track
///     references, so a self-referring graph would be serialized without end.
///     A failure travels as itself only when its structure is known to be finite and plain, and serializing it with this
///     process's Orleans serializer succeeds. The structure check is deliberately conservative: an exception whose
///     <see cref="Exception.Data" /> holds anything but simple values (strings, primitives, enums, decimal, dates,
///     time spans, Guids) does not travel as itself. Any failure that does not travel is replaced: a cancellation by an
///     <see cref="OperationCanceledException" /> with the same message, anything else by the form these grain contracts
///     always delivered, an <see cref="Exception" /> whose message is <c>[TypeName] message</c>. A failure that travels
///     is left as the same instance.
/// </summary>
internal static class GrainFailureTransport
{
    private const int MaxDepth = 64;
    private const int MaxExceptions = 1024;

    /// <summary>Awaits <paramref name="task" /> and lets its failure travel as described on the type.</summary>
    internal static async Task<T> PrepareAsync<T>(Task<T> task, Func<IServiceProvider?> services)
    {
        try
        {
            return await task;
        }
        catch (Exception ex) when (!CanTravel(ex, ResolveServices(services)))
        {
            throw ToTransportableForm(ex);
        }
    }

    /// <summary>
    ///     True when the exception travels as itself. A grain that is not attached to a runtime (created directly in a unit
    ///     test) has no serializer to judge with: its failure is left untouched. Never throws.
    /// </summary>
    internal static bool CanTravel(Exception exception, IServiceProvider? services)
    {
        Serializer? serializer;
        try
        {
            serializer = services?.GetService<Serializer>();
        }
        catch (Exception)
        {
            serializer = null;
        }

        if (serializer is null)
        {
            return true;
        }

        // Only a finite, acyclic structure with simple Data reaches the serializer, so it cannot recurse without end.
        if (!HasPlainStructure(exception))
        {
            return false;
        }

        try
        {
            serializer.SerializeToArray(exception);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The form a failure that cannot travel is delivered in.</summary>
    internal static Exception ToTransportableForm(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            // Callers classify cancellation by type; keep it a cancellation, without the contents that could not travel.
            return exception is TaskCanceledException
                ? new TaskCanceledException(exception.Message)
                : new OperationCanceledException(exception.Message);
        }

        var typeName = exception.GetType().Name;
        var message = exception.Message;
        return !string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(message)
            ? new Exception($"[{typeName}] {message}")
            : new Exception(message ?? "Unknown error");
    }

    // The grain's services are resolved only when a failure needs judging. A grain that is not attached to a runtime
    // (created directly in a unit test) may have none, or a runtime double that cannot provide them.
    private static IServiceProvider? ResolveServices(Func<IServiceProvider?> services)
    {
        try
        {
            return services();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Walks only the exception structure: inner exception(s) and Data entries, which must all be simple values. An
    // exception with a Sekiban converter travels through its surrogate, which carries only its own fields and, for some
    // types, the inner exception; its Data is not serialized and is not inspected. Every exception is visited at most
    // once and the walk is bounded in depth and size; reaching an exception twice, or any bound, fails the check.
    private static bool HasPlainStructure(Exception root)
    {
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(Exception Exception, int Depth)>();
        pending.Push((root, 0));
        try
        {
            while (pending.Count > 0)
            {
                var (exception, depth) = pending.Pop();
                if (depth > MaxDepth || !visited.Add(exception) || visited.Count > MaxExceptions)
                {
                    return false;
                }

                var converter = Converter(exception);
                if (converter == ConverterKind.None && !HasSimpleData(exception))
                {
                    return false;
                }

                if (converter == ConverterKind.WithoutInnerException)
                {
                    continue;
                }

                if (exception is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        pending.Push((inner, depth + 1));
                    }
                }
                else if (exception.InnerException is { } inner)
                {
                    pending.Push((inner, depth + 1));
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private enum ConverterKind
    {
        None,
        WithoutInnerException,
        WithInnerException
    }

    // The exception types the converters in Sekiban.Dcb.Orleans.Serialization carry, and whether their surrogate
    // carries the inner exception.
    private static ConverterKind Converter(Exception exception) => exception switch
    {
        SekibanProjectionFaultException or ExpectedTagPositionConflictException or TagHeadExpectationValidationException =>
            ConverterKind.WithoutInnerException,
        ConditionalAppendInDoubtException or KeyReuseConflictException => ConverterKind.WithInnerException,
        _ => ConverterKind.None
    };

    private static bool HasSimpleData(Exception exception)
    {
        var data = exception.Data;
        if (data is null || data.Count == 0)
        {
            return true;
        }

        foreach (DictionaryEntry entry in data)
        {
            if (!IsSimple(entry.Key) || !IsSimple(entry.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSimple(object? value)
    {
        if (value is null or string)
        {
            return true;
        }

        var type = value.GetType();
        return type.IsPrimitive || type.IsEnum || value is decimal or DateTime or DateTimeOffset or TimeSpan or Guid;
    }
}
