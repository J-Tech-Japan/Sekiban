import {
  ICommandWithHandler,
  ICommandContext,
  EmptyAggregatePayload,
  Aggregate,
  IEventPayload,
  PartitionKeys,
  z
} from '@sekiban/core';
import { Result, ok, err } from 'neverthrow';
import type { WeatherForecastPayload, WeatherForecastProjector } from './weather-forecast-projector.js';

/**
 * Event payload for weather forecast input
 */
export interface WeatherForecastInputted extends IEventPayload {
  eventType: 'WeatherForecastInputted';
  location: string;
}

/**
 * Command to input weather forecast
 */
export class InputWeatherForecastCommand implements ICommandWithHandler<
  { location: string },
  WeatherForecastProjector,
  WeatherForecastPayload,
  EmptyAggregatePayload
> {
  readonly commandType = 'InputWeatherForecast';

  // Schema for command validation
  private schema = z.object({
    location: z.string().min(1, 'Location is required')
  });

  specifyPartitionKeys(data: { location: string }): PartitionKeys {
    return new PartitionKeys(
      `weather-${data.location.toLowerCase().replace(/\s+/g, '-')}`,
      'WeatherForecast',
      'default'
    );
  }

  validate(data: { location: string }): Result<void, Error> {
    const result = this.schema.safeParse(data);
    if (!result.success) {
      return err(new Error(result.error.issues[0].message));
    }
    return ok(undefined);
  }

  async handle(
    context: ICommandContext,
    data: { location: string },
    aggregate: Aggregate<EmptyAggregatePayload>
  ): Promise<Result<IEventPayload[], Error>> {
    if (aggregate.version > 0) {
      return err(new Error('Weather forecast already exists for this location'));
    }

    const event: WeatherForecastInputted = {
      eventType: 'WeatherForecastInputted',
      location: data.location
    };

    return ok([event]);
  }

  getProjector(): WeatherForecastProjector {
    return new (require('./weather-forecast-projector.js').WeatherForecastProjector)();
  }
}