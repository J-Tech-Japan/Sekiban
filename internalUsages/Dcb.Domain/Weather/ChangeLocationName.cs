using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Weather;

public record ChangeLocationName : ICommandWithHandler<ChangeLocationName>
{
    [Required]
    public Guid ForecastId { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string NewLocationName { get; init; } = string.Empty;

    public static Task<ResultBox<EventOrNone>> HandleAsync(ChangeLocationName command, ICommandContext context) =>
        ResultBox
            .Start
            .Remap(_ => new WeatherForecastTag(command.ForecastId))
            .Combine(tag => context.TagExistsAsync(tag))
            .Verify((tag, exists) => exists
                ? ExceptionOrNone.None
                : ExceptionOrNone.FromException(
                    new ApplicationException($"Weather forecast {command.ForecastId} does not exist")))
            .Combine((tag, _) => context.GetStateAsync<WeatherForecastProjector>(tag))
            .Verify((_, _, state) =>
            {
                var payload = state.Payload as WeatherForecastState;
                return payload?.IsDeleted == true
                    ? ExceptionOrNone.FromException(
                        new ApplicationException($"Weather forecast {command.ForecastId} has been deleted"))
                    : ExceptionOrNone.None;
            })
            .Conveyor((tag, _, state) =>
            {
                var payload = state.Payload as WeatherForecastState;
                return payload?.Location == command.NewLocationName
                    ? EventOrNone.None
                    : EventOrNone.EventWithTags(
                        new LocationNameChanged(
                            command.ForecastId,
                            command.NewLocationName,
                            payload?.Location ?? string.Empty),
                        tag);
            });

}
