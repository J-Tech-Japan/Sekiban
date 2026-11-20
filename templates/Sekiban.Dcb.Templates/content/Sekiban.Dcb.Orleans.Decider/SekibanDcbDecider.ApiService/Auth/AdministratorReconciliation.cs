namespace SekibanDcbDecider.ApiService.Auth;

/// <summary>
///     The Identity Admin role is the source of truth for administrators. The domain keeps its own copy in
///     UserAccess (used by commands, for example the organizer-or-admin rule and the monthly limit bypass).
///     This works out which domain users must gain or lose the Admin role so both agree.
/// </summary>
public static class AdministratorReconciliation
{
    public const string AdminRole = "Admin";

    public sealed record Plan(IReadOnlyList<Guid> Grant, IReadOnlyList<Guid> Revoke);

    public static Plan Create(IEnumerable<Guid> identityAdministrators, IEnumerable<Guid> domainAdministrators)
    {
        var identity = identityAdministrators.ToHashSet();
        var domain = domainAdministrators.ToHashSet();
        return new Plan(
            [.. identity.Where(id => !domain.Contains(id)).Order()],
            [.. domain.Where(id => !identity.Contains(id)).Order()]);
    }
}
