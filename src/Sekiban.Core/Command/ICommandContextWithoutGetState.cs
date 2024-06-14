using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
namespace Sekiban.Core.Command;

public interface ICommandContextWithoutGetState<TAggregatePayload> where TAggregatePayload : IAggregatePayloadCommon
{
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class;
    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class;
    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>() where T1 : class where T2 : class where T3 : class;
    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class;

    public ResultBox<UnitValue> AppendEvent(IEventPayloadApplicableTo<TAggregatePayload> eventPayload);
}