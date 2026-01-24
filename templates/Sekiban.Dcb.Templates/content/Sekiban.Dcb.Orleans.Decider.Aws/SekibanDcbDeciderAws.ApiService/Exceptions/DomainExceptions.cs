namespace SekibanDcbDeciderAws.ApiService.Exceptions;

/// <summary>
/// Base exception for all domain-related errors
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a requested resource is not found
/// </summary>
public class NotFoundException : DomainException
{
    public NotFoundException(string resourceType, object resourceId)
        : base($"{resourceType} with ID '{resourceId}' was not found.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

    public string ResourceType { get; }
    public object ResourceId { get; }
}

/// <summary>
/// Exception thrown when a business rule validation fails
/// </summary>
public class ValidationException : DomainException
{
    public ValidationException(string message) : base(message) { }

    public ValidationException(string message, Dictionary<string, string[]> errors) : base(message)
    {
        Errors = errors;
    }

    public Dictionary<string, string[]>? Errors { get; }
}

/// <summary>
/// Exception thrown when a resource already exists
/// </summary>
public class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>
/// Exception thrown when an operation is not authorized
/// </summary>
public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message) : base(message) { }
}
