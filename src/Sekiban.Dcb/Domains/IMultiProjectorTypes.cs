using System;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Registry and dispatcher for multi projectors in DCB.
/// </summary>
public interface IMultiProjectorTypes
{
    ResultBox<IMultiProjectorCommon> GetMultiProjector(string multiProjectorName);
}
