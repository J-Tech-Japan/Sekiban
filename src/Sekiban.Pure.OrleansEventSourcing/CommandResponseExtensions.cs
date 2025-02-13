using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.OrleansEventSourcing;

public static class CommandResponseExtensions
{
    public static OrleansCommandResponse ToOrleansCommandResponse(this CommandResponse response) =>
        new(
            response.PartitionKeys.ToOrleansPartitionKeys(),
            response.Events.Select(OrleansEvent.FromEvent).ToList(),
            response.Version);
    public static CommandResponse ToCommandResponse(this OrleansCommandResponse response, IEventTypes eventTypes) =>
        new(
            response.PartitionKeys.ToPartitionKeys(),
            response
                .Events
                .Select(e => e.ToEvent(eventTypes))
                .Where(r => r.IsSuccess)
                .Select(r => r.GetValue())
                .ToList(),
            response.Version);
}