using System;
using System.Collections.Generic;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Registry and dispatcher for multi projectors in DCB.
/// </summary>
public interface IMultiProjectorTypes
{
    ResultBox<IMultiProjectionPayload> Project(string multiProjectorName, IMultiProjectionPayload payload, Event ev, List<ITag> tags);
    
    ResultBox<string> GetProjectorVersion(string multiProjectorName);
    
    ResultBox<Func<IMultiProjectionPayload>> GetInitialPayloadGenerator(string multiProjectorName);
    
    ResultBox<Type> GetProjectorType(string multiProjectorName);
    
    ResultBox<IMultiProjectionPayload> GenerateInitialPayload(string multiProjectorName);
    
    ResultBox<IMultiProjectionPayload> Deserialize(byte[] data, string multiProjectorName, System.Text.Json.JsonSerializerOptions jsonOptions);
}