using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput> where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon;
