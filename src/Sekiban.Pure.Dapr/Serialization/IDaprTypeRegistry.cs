namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
///     Interface for type registration and resolution in Dapr serialization
/// </summary>
public interface IDaprTypeRegistry
{
    /// <summary>
    ///     Registers a type with an alias for serialization
    /// </summary>
    /// <typeparam name="T">Type to register</typeparam>
    /// <param name="typeAlias">Alias for the type</param>
    void RegisterType<T>(string typeAlias) where T : class;

    /// <summary>
    ///     Registers a type with an alias for serialization
    /// </summary>
    /// <param name="type">Type to register</param>
    /// <param name="typeAlias">Alias for the type</param>
    void RegisterType(Type type, string typeAlias);

    /// <summary>
    ///     Resolves a type from its alias
    /// </summary>
    /// <param name="typeAlias">Type alias</param>
    /// <returns>The resolved type, or null if not found</returns>
    Type? ResolveType(string typeAlias);

    /// <summary>
    ///     Gets the alias for a type
    /// </summary>
    /// <param name="type">Type to get alias for</param>
    /// <returns>The type alias</returns>
    string GetTypeAlias(Type type);

    /// <summary>
    ///     Checks if a type is registered
    /// </summary>
    /// <param name="type">Type to check</param>
    /// <returns>True if registered</returns>
    bool IsTypeRegistered(Type type);

    /// <summary>
    ///     Checks if an alias is registered
    /// </summary>
    /// <param name="typeAlias">Alias to check</param>
    /// <returns>True if registered</returns>
    bool IsAliasRegistered(string typeAlias);
}
