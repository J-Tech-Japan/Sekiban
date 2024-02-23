using Sekiban.Core.Documents;
using Sekiban.Core.History;
namespace Sekiban.Core.Command;

/// <summary>
///     Common interface for the command document.
///     Internal use only.
/// </summary>
public interface ICommandDocumentCommon : IAggregateDocument, ICallHistories
{

    /// <summary>
    ///     Executed user can be set by implementing <see cref="IUserInformationFactory" />
    /// </summary>
    public string? ExecutedUser { get; }
    /// <summary>
    ///     Exception message will be set when an exception occurs during command execution.
    /// </summary>
    public string? Exception { get; }
    /// <summary>
    ///     Get Payload common interface
    /// </summary>
    /// <returns></returns>
    public ICommandCommon GetPayload();
    /// <summary>
    ///     Get Typed Payload
    /// </summary>
    /// <typeparam name="TCommandPayload"></typeparam>
    /// <returns></returns>
    public TCommandPayload? GetPayload<TCommandPayload>() where TCommandPayload : class, ICommandCommon;
}
