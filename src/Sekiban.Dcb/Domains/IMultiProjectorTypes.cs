using System;
using System.Collections.Generic;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Registry and dispatcher for multi projectors in DCB.
/// </summary>
public interface IMultiProjectorTypes
{
    ResultBox<IMultiProjectorCommon> GetMultiProjector(string multiProjectorName);

    ResultBox<IMultiProjectorCommon> Project(IMultiProjectorCommon multiProjector, Event ev);

    ResultBox<IMultiProjectorCommon> Project(IMultiProjectorCommon multiProjector, IReadOnlyList<Event> events)
    {
        var acc = ResultBox.FromValue(multiProjector);
        foreach (var e in events)
        {
            if (!acc.IsSuccess) return acc;
            var current = acc.GetValue();
            acc = Project(current, e);
        }
        return acc;
    }

    ResultBox<IMultiProjectorCommon> GenerateInitialPayload(string multiProjectorName);

    ResultBox<byte[]> Serialize(IMultiProjectorCommon multiProjector, JsonSerializerOptions options);

    ResultBox<IMultiProjectorCommon> Deserialize(byte[] jsonBytes, string payloadTypeFullName, JsonSerializerOptions options);

    ResultBox<string> GetMultiProjectorNameFromMultiProjector(IMultiProjectorCommon multiProjector);
}
