using DcbLib.Tags;

namespace DcbLib.Actors;

public interface ITagStateActorCommon
{
    SerializableTagState GetState();
    string GetTagStateActorId();
}