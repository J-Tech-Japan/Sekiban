using System.Collections.Immutable;
using Orleans;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Events;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

namespace DaprSekiban.Domain.Projections.Count;
[GenerateSerializer]
public record WeatherCountMultiProjection(ImmutableDictionary<string, int> WeatherCounts, ImmutableDictionary<PartitionKeys, string> LocationDictionary) : IMultiProjector<WeatherCountMultiProjection>
{
    public ResultBox<WeatherCountMultiProjection> Project(WeatherCountMultiProjection payload, IEvent ev)
        => ev.GetPayload() switch
        {
            WeatherForecastInputted inputted => new WeatherCountMultiProjection(
                payload.WeatherCounts.ContainsKey(inputted.Location) ? payload.WeatherCounts.SetItem(inputted.Location,payload.WeatherCounts[inputted.Location] + 1) : payload.WeatherCounts.Add(inputted.Location,1), 
                payload.LocationDictionary.Add(ev.PartitionKeys, inputted.Location)),
            WeatherForecastDeleted => new WeatherCountMultiProjection(
                payload.WeatherCounts.ContainsKey(payload.LocationDictionary[ev.PartitionKeys]) ? payload.WeatherCounts.SetItem(payload.LocationDictionary[ev.PartitionKeys], payload.WeatherCounts[payload.LocationDictionary[ev.PartitionKeys]] - 1) : payload.WeatherCounts,
                payload.LocationDictionary.Remove(ev.PartitionKeys)),
            WeatherForecastLocationUpdated updated => UpdateForecastLocation( updated, ev, payload),
            _ => payload
        };

    private static WeatherCountMultiProjection UpdateForecastLocation(WeatherForecastLocationUpdated location, IEvent ev,
        WeatherCountMultiProjection payload)
    {
        var toUpdate = payload;
        // find before location and remove count
        var beforeLocation = toUpdate.LocationDictionary[ev.PartitionKeys];
        if (toUpdate.WeatherCounts.ContainsKey(beforeLocation))
        {
            toUpdate = toUpdate with {WeatherCounts =  toUpdate.WeatherCounts.SetItem(beforeLocation, toUpdate.WeatherCounts[beforeLocation] - 1)};
        }
        toUpdate = toUpdate with {LocationDictionary = toUpdate.LocationDictionary.Remove(ev.PartitionKeys)};
        
        // add new location and add count
        
        if (toUpdate.WeatherCounts.ContainsKey(location.NewLocation))
        {
            toUpdate = toUpdate with {WeatherCounts =  toUpdate.WeatherCounts.SetItem(location.NewLocation, toUpdate.WeatherCounts[location.NewLocation] + 1)};
        }
        else
        {
            toUpdate = toUpdate with {WeatherCounts =  toUpdate.WeatherCounts.Add(location.NewLocation, 1)};
        }
        toUpdate = toUpdate with {LocationDictionary = toUpdate.LocationDictionary.Add(ev.PartitionKeys, location.NewLocation)};
        return toUpdate;
    }
    public static WeatherCountMultiProjection GenerateInitialPayload()
    {
        return new WeatherCountMultiProjection(ImmutableDictionary<string, int>.Empty, ImmutableDictionary<PartitionKeys, string>.Empty);
    }

    public static string GetMultiProjectorName()
    {
        return nameof(WeatherCountMultiProjection);
    }
}