using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Tags;

/// <summary>
/// Non-generic base interface for tag projectors (for backward compatibility)
/// </summary>
public interface ITagProjector
{
    // This interface exists for backward compatibility
    // All actual implementations should use ITagProjector<T>
}

/// <summary>
/// Generic interface for tag projectors with static members
/// </summary>
public interface ITagProjector<TProjector> : ITagProjector 
    where TProjector : ITagProjector<TProjector>
{
    static abstract string ProjectorVersion { get; }
    static abstract string ProjectorName { get; }
    static abstract ITagStatePayload Project(ITagStatePayload current, Event ev);
}
