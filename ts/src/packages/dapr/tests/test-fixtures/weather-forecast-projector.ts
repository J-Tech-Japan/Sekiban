import { 
  AggregateProjector, 
  Aggregate, 
  EmptyAggregatePayload,
  IEvent,
  SekibanError,
  PartitionKeys
} from '@sekiban/core';
import { Result, ok, err } from 'neverthrow';

/**
 * Test payload for weather forecast
 */
export interface WeatherForecastPayload {
  aggregateType: 'WeatherForecast';
  location: string;
  temperature?: number;
}

/**
 * Test projector for weather forecast aggregate
 */
export class WeatherForecastProjector extends AggregateProjector<WeatherForecastPayload> {
  readonly aggregateTypeName = 'WeatherForecast';

  getInitialState(partitionKeys: PartitionKeys): Aggregate<EmptyAggregatePayload> {
    return new Aggregate(
      partitionKeys,
      this.aggregateTypeName,
      0,
      new EmptyAggregatePayload(),
      null,
      this.constructor.name,
      this.getVersion()
    );
  }

  project(
    aggregate: Aggregate<WeatherForecastPayload | EmptyAggregatePayload>,
    event: IEvent
  ): Result<Aggregate<WeatherForecastPayload | EmptyAggregatePayload>, SekibanError> {
    if (event.eventType === 'WeatherForecastInputted') {
      const payload = event.payload as { location: string };
      const newPayload: WeatherForecastPayload = {
        aggregateType: 'WeatherForecast',
        location: payload.location
      };
      return ok(this.createUpdatedAggregate(aggregate, newPayload, event));
    }
    
    return ok(aggregate);
  }

  canHandle(eventType: string): boolean {
    return eventType === 'WeatherForecastInputted';
  }

  getSupportedPayloadTypes(): string[] {
    return ['WeatherForecast'];
  }
}