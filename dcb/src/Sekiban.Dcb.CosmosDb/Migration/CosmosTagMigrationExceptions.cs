namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     Raised when a destructive run is attempted without the operator having said so.
///     There is no way to delete a tag row by omission: the confirm flag has to be set, and the backup writer
///     has to be supplied, or the run refuses before touching a single document.
/// </summary>
public class CosmosTagMigrationNotAuthorizedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CosmosTagMigrationNotAuthorizedException()
        : base("The destructive tag migration was not authorized.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public CosmosTagMigrationNotAuthorizedException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public CosmosTagMigrationNotAuthorizedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
///     Raised when the plan handed to a destructive run is not one this run may act on: missing, built for a
///     different lineage, or altered since it was produced.
///     A destructive run takes a plan and nothing else, so an operator cannot delete rows they were not first
///     shown. That only means something if the plan is the one they were shown — hence this.
/// </summary>
public class CosmosTagMigrationPlanRejectedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CosmosTagMigrationPlanRejectedException()
        : base("The destructive tag migration plan was rejected.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public CosmosTagMigrationPlanRejectedException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public CosmosTagMigrationPlanRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
