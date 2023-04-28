using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Aggregate;

public interface IAggregateStateCommon : IAggregateCommon
{
    public dynamic GetPayload();
}
