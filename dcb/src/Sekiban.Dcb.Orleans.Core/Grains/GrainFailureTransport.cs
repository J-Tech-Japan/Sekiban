using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Prepares a failure that a grain contract reports by faulting its task for the trip to the caller. Orleans
///     serializes the exception itself, so its type and message survive the hop (and, for the Sekiban exception types
///     with a registered converter, the properties callers read). Orleans cannot carry an exception it has no codec for,
///     anywhere in its inner-exception chain or <see cref="Exception.Data" />: the call would fail with a codec error that
///     drops the message. Only such a failure is replaced, by the form these grain contracts always delivered for it: an
///     <see cref="Exception" /> whose message is <c>[TypeName] message</c>.
/// </summary>
internal static class GrainFailureTransport
{
    private const int MaxInspectedExceptions = 32;

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
    ///     runtime (created directly in a unit test) has no codec provider to judge with: its failure is left untouched.
    /// </summary>
    internal static bool CanTravel(Exception exception, IServiceProvider? services)
    {
        ICodecProvider? codecs;
        try
        {
            codecs = services?.GetService<ICodecProvider>();
        }
        catch (Exception)
        {
            codecs = null;
        }

        if (codecs is null)
        {
            return true;
        }

        var pending = new Stack<Exception>();
        pending.Push(exception);
        var inspected = 0;
        while (pending.Count > 0)
        {
            if (++inspected > MaxInspectedExceptions)
            {
                return false;
            }

            var current = pending.Pop();
            var codec = TryGetCodec(codecs, current.GetType());
            if (codec is null)
            {
                return false;
            }

            if (codec is ExceptionCodec && !DataCanTravel(codecs, current))
            {
                return false;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }

        return true;
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

    /// <summary>The <c>[TypeName] message</c> form a failure without a codec travels in.</summary>
    internal static Exception ToTransportableForm(Exception exception)
    {
        var typeName = exception.GetType().Name;
        var message = exception.Message;
        return !string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(message)
            ? new Exception($"[{typeName}] {message}")
            : new Exception(message ?? "Unknown error");
    }

    private static bool DataCanTravel(ICodecProvider codecs, Exception exception)
    {
        if (exception.Data.Count == 0)
        {
            return true;
        }

        foreach (System.Collections.DictionaryEntry entry in exception.Data)
        {
            if (TryGetCodec(codecs, entry.Key.GetType()) is null ||
                (entry.Value is not null && TryGetCodec(codecs, entry.Value.GetType()) is null))
            {
                return false;
            }
        }

        return true;
    }

    private static IFieldCodec? TryGetCodec(ICodecProvider codecs, Type type)
    {
        try
        {
            return codecs.TryGetCodec(type);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
