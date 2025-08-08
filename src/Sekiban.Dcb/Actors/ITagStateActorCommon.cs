using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

public interface ITagStateActorCommon
{
    Task<SerializableTagState> GetStateAsync();
    Task<string> GetTagStateActorIdAsync();
}
