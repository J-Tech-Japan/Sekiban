using Sekiban.Core.Command.UserInformation;
namespace Sekiban.Core.History;

/// <summary>
///     Call History
/// </summary>
/// <param name="Id">Usually Command Id or Event Id, referring document Id </param>
/// <param name="TypeName">Document Type Name</param>
/// <param name="ExecutedUser">Executed User retrieved from <see cref="IUserInformationFactory" /> </param>
public record CallHistory(Guid Id, string TypeName, string? ExecutedUser);
