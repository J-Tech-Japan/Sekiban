import { 
  defineProjector, 
  EmptyAggregatePayload, 
  AggregateProjector, 
  PartitionKeys, 
  Aggregate, 
  IEvent, 
  Result, 
  ok, 
  SekibanError 
} from '@sekiban/core';
import { z } from 'zod';
import { 
  WeatherForecastInputted, 
  WeatherForecastLocationUpdated, 
  WeatherForecastDeleted 
} from '../events/weather-forecast-events.js';
import { 
  WeatherForecastState, 
  DeletedWeatherForecastState, 
  WeatherForecastPayloadUnion 
} from '../payloads/weather-forecast-payloads.js';

export const weatherForecastProjectorDefinition = defineProjector<WeatherForecastPayloadUnion>({
  aggregateType: 'WeatherForecast',
  
  initialState: () => new EmptyAggregatePayload(),
  
  projections: {
    WeatherForecastInputted: (state: any, event: z.infer<typeof WeatherForecastInputted.schema>) => ({
      aggregateType: 'WeatherForecast' as const,
      location: event.location,
      date: event.date,
      temperatureC: event.temperatureC,
      summary: event.summary
    } as WeatherForecastState),
    
    WeatherForecastLocationUpdated: (state: any, event: z.infer<typeof WeatherForecastLocationUpdated.schema>) => {
      if (!state || state.aggregateType !== 'WeatherForecast') return state;
      return {
        ...state,
        location: event.location
      } as WeatherForecastState;
    },
    
    WeatherForecastDeleted: (state: any, event: z.infer<typeof WeatherForecastDeleted.schema>) => {
      if (!state || state.aggregateType !== 'WeatherForecast') return state;
      return {
        aggregateType: 'DeletedWeatherForecast' as const
      } as DeletedWeatherForecastState;
    }
  }
});

export class WeatherForecastProjector extends AggregateProjector<WeatherForecastPayloadUnion> {
  readonly aggregateTypeName = 'WeatherForecast';
  readonly multiProjectorName = 'WeatherForecastAggregateListProjector';
  
  getInitialState(partitionKeys: PartitionKeys): Aggregate<EmptyAggregatePayload> {
    return weatherForecastProjectorDefinition.getInitialState(partitionKeys);
  }
  
  project(
    aggregate: Aggregate<WeatherForecastPayloadUnion | EmptyAggregatePayload>, 
    event: IEvent
  ): Result<Aggregate<WeatherForecastPayloadUnion | EmptyAggregatePayload>, SekibanError> {
    return weatherForecastProjectorDefinition.project(aggregate, event);
  }
  
  canHandle(eventType: string): boolean {
    return [
      'WeatherForecastInputted',
      'WeatherForecastLocationUpdated',
      'WeatherForecastDeleted'
    ].includes(eventType);
  }
  
  getSupportedPayloadTypes(): string[] {
    return ['WeatherForecast', 'DeletedWeatherForecast'];
  }
}