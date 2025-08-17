using System;
using System.Collections.Generic;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Registry and dispatcher for multi projectors in DCB.
/// </summary>
public interface IMultiProjectorTypes
{
    ResultBox<Func<object, Event, List<ITag>, ResultBox<object>>> GetProjectorFunction(string multiProjectorName);
    
    ResultBox<string> GetProjectorVersion(string multiProjectorName);
    
    ResultBox<Func<object>> GetInitialPayloadGenerator(string multiProjectorName);
    
    ResultBox<Type> GetProjectorType(string multiProjectorName);
}