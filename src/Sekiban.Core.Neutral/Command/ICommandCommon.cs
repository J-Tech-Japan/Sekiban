using Sekiban.Core.Documents;
namespace Sekiban.Core.Command;

/// <summary>
///     system use for common command interface.
///     Application developer does not need to use this class.
/// </summary>
public interface ICommandCommon
{
    /// <summary>
    ///     Set root partition key for the command.
    /// </summary>
    /// <returns>root partition key</returns>
    public string GetRootPartitionKey() => IDocument.DefaultRootPartitionKey;
}
