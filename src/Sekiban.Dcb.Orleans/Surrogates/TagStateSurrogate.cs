using Orleans;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Orleans.Surrogates;

[GenerateSerializer]
public struct TagStateSurrogate
{
    [Id(0)]
    public object? Payload { get; set; }
    
    [Id(1)]
    public int Version { get; set; }
    
    [Id(2)]
    public string LastSortedUniqueId { get; set; }
    
    [Id(3)]
    public string TagGroup { get; set; }
    
    [Id(4)]
    public string TagContent { get; set; }
    
    [Id(5)]
    public string TagProjector { get; set; }
    
    [Id(6)]
    public string ProjectorVersion { get; set; }
}

[RegisterConverter]
public sealed class TagStateSurrogateConverter : IConverter<TagState, TagStateSurrogate>
{
    public TagState ConvertFromSurrogate(in TagStateSurrogate surrogate)
    {
        // Note: We're casting the payload back to ITagStatePayload
        // This assumes the object was properly serialized
        return new TagState(
            (ITagStatePayload)surrogate.Payload!,
            surrogate.Version,
            surrogate.LastSortedUniqueId,
            surrogate.TagGroup,
            surrogate.TagContent,
            surrogate.TagProjector,
            surrogate.ProjectorVersion);
    }

    public TagStateSurrogate ConvertToSurrogate(in TagState value)
    {
        return new TagStateSurrogate
        {
            Payload = value.Payload,
            Version = value.Version,
            LastSortedUniqueId = value.LastSortedUniqueId,
            TagGroup = value.TagGroup,
            TagContent = value.TagContent,
            TagProjector = value.TagProjector,
            ProjectorVersion = value.ProjectorVersion
        };
    }
}