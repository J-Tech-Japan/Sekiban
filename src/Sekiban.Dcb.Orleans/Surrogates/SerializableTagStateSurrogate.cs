using Orleans;

namespace Sekiban.Dcb.Orleans.Surrogates;

[GenerateSerializer]
public struct SerializableTagStateSurrogate
{
    [Id(0)]
    public byte[] Payload { get; set; }
    
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
    public string TagPayloadName { get; set; }
    
    [Id(7)]
    public string ProjectorVersion { get; set; }
}