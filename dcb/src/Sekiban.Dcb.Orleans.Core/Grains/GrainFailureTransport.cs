using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Prepares a failure that a grain contract reports by faulting its task for the trip to the caller. Orleans
///     serializes the exception itself, so its type and message survive the hop (and, for the Sekiban exception types
///     with a registered converter, the properties callers read). Orleans cannot carry every exception: a type it has no
///     codec for, anywhere in the exception graph, makes the call fail with a codec error that drops the message, and a
///     graph that refers back to itself (an exception in its own <see cref="Exception.Data" />) is never finished.
///     Whether the failure can travel is decided by serializing it with this process's Orleans serializer. Only a
///     failure that cannot is replaced: a cancellation by an <see cref="OperationCanceledException" /> with the same
///     message, anything else by the form these grain contracts always delivered for it, an <see cref="Exception" />
///     whose message is <c>[TypeName] message</c>. A failure that can travel is left as the same instance.
/// </summary>
internal static class GrainFailureTransport
{
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
    ///     True when Orleans in this process can serialize the exception as it is. A grain that is not attached to a
    ///     runtime (created directly in a unit test) has no serializer to judge with: its failure is left untouched.
    ///     Never throws.
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

        // The exception codec does not track references, so a cycle would be serialized without end.
        if (HasCycle(exception))
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

    // A depth-first walk over the exceptions the exception codec follows from an exception: its inner exception(s) and
    // exceptions held in its Data, as a key, a value or an item of a collection value. A path that reaches an exception
    // already on the path is a cycle. A graph that cannot be walked counts as a cycle, so it never reaches the
    // serializer.
    private static bool HasCycle(Exception root)
    {
        var onPath = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var finished = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(Exception Node, IEnumerator<Exception> Children)>();
        try
        {
            onPath.Add(root);
            stack.Push((root, Children(root).GetEnumerator()));
            while (stack.Count > 0)
            {
                var (node, children) = stack.Peek();
                if (!children.MoveNext())
                {
                    stack.Pop();
                    onPath.Remove(node);
                    finished.Add(node);
                    continue;
                }

                var child = children.Current;
                if (onPath.Contains(child))
                {
                    return true;
                }

                if (finished.Contains(child))
                {
                    continue;
                }

                onPath.Add(child);
                stack.Push((child, Children(child).GetEnumerator()));
            }

            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static IEnumerable<Exception> Children(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                yield return inner;
            }
        }
        else if (exception.InnerException is { } inner)
        {
            yield return inner;
        }

        if (exception.Data is not { Count: > 0 } data)
        {
            yield break;
        }

        foreach (DictionaryEntry entry in data)
        {
            foreach (var held in Held(entry.Key).Concat(Held(entry.Value)))
            {
                yield return held;
            }
        }
    }

    private static IEnumerable<Exception> Held(object? value) => value switch
    {
        Exception exception => [exception],
        string => [],
        IEnumerable items => items.OfType<Exception>(),
        _ => []
    };
}
