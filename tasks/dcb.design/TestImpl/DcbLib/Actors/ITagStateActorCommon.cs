using DcbLib.Tags;

namespace DcbLib.Actors;

public interface ITagStateActorCommon
{
    Task<SerializableTagState> GetStateAsync();
    Task<string> GetTagStateActorIdAsync();
}