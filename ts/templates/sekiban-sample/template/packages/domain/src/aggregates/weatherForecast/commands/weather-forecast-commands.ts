import { z } from 'zod';
import { command, PartitionKeys } from '@sekiban/core';
import { WeatherForecastProjector } from '../projectors/weather-forecast-projector.js';
import { type WeatherForecastPayloadUnion, type WeatherForecastState } from '../payloads/weather-forecast-payloads.js';
import { WeatherForecastInputted, WeatherForecastLocationUpdated, WeatherForecastDeleted } from '../events/weather-forecast-events.js';
import { TemperatureCelsiusSchema, createTemperatureCelsius } from '../../../valueObjects/temperature-celsius.js';

export const InputWeatherForecast = command.create('InputWeatherForecast', {
  schema: z.object({
    location: z.string(),
    date: z.string(),
    temperatureC: z.number(),
    summary: z.string().optional()
  }),
  projector: new WeatherForecastProjector(),
  partitionKeys: () => PartitionKeys.generate('WeatherForecast'),
  handle: (data, { appendEvent }) => {
    const tempCelsius = createTemperatureCelsius(data.temperatureC);
    
    appendEvent(WeatherForecastInputted.create({
      location: data.location,
      date: data.date,
      temperatureC: tempCelsius,
      summary: data.summary
    }));
  }
});

export const UpdateWeatherForecastLocation = command.update('UpdateWeatherForecastLocation', {
  schema: z.object({
    weatherForecastId: z.string().uuid(),
    location: z.string()
  }),
  projector: new WeatherForecastProjector(),
  partitionKeys: (data) => PartitionKeys.existing(data.weatherForecastId, 'WeatherForecast'),
  handle: (data, { aggregate, appendEvent }) => {
    const weatherForecast = aggregate as WeatherForecastPayloadUnion | undefined;
    
    if (!weatherForecast || weatherForecast.aggregateType !== 'WeatherForecast') {
      throw new Error('WeatherForecast not found or is deleted');
    }
    
    appendEvent(WeatherForecastLocationUpdated.create({
      location: data.location
    }));
  }
});

export const DeleteWeatherForecast = command.update('DeleteWeatherForecast', {
  schema: z.object({
    weatherForecastId: z.string().uuid()
  }),
  projector: new WeatherForecastProjector(),
  partitionKeys: (data) => PartitionKeys.existing(data.weatherForecastId, 'WeatherForecast'),
  handle: (data, { aggregate, appendEvent }) => {
    const weatherForecast = aggregate as WeatherForecastPayloadUnion | undefined;
    
    if (!weatherForecast || weatherForecast.aggregateType !== 'WeatherForecast') {
      throw new Error('WeatherForecast not found or is already deleted');
    }
    
    appendEvent(WeatherForecastDeleted.create({}));
  }
});

export const RemoveWeatherForecast = DeleteWeatherForecast;