using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.States.Weather;
using Dcb.ImmutableModels.States.Weather.Deciders;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Decider.Weather;

public record ChangeLocationName : ICommandWithHandler<ChangeLocationName>
{
    [Required]
    public Guid ForecastId { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string NewLocationName { get; init; } = string.Empty;

    public static async Task<EventOrNone> HandleAsync(
        ChangeLocationName command,
        ICommandContext context)
    {
        var tag = new WeatherForecastTag(command.ForecastId);
        var exists = await context.TagExistsAsync(tag);
        if (!exists)
        {
            throw new ApplicationException($"Weather forecast {command.ForecastId} does not exist");
        }

        var state = await context.GetStateAsync<WeatherForecastState, WeatherForecastProjector>(tag);

        // Use Decider.Validate - handles both IsDeleted check and same-name check
        try
        {
            state.Payload.Validate(command.NewLocationName);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already"))
        {
            // Same location name - return empty (idempotent)
            return EventOrNone.Empty;
        }

        return new LocationNameChanged(
            command.ForecastId,
            command.NewLocationName,
            state.Payload.Location).GetEventWithTags();
    }
}
