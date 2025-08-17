using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Tags;

/// <summary>
///     Generic interface for tag projectors with static members
/// </summary>
public interface ITagProjector<TProjector> where TProjector : ITagProjector<TProjector>
{
    static abstract string ProjectorVersion { get; }
    static abstract string ProjectorName { get; }
    static abstract ITagStatePayload Project(ITagStatePayload current, Event ev);
}
