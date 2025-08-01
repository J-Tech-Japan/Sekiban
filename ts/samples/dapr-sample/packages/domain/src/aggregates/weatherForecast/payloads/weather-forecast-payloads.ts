import { z } from 'zod';
import { TemperatureCelsiusSchema } from '../../../valueObjects/temperature-celsius.js';

export interface WeatherForecastState {
  aggregateType: 'WeatherForecast';
  location: string;
  date: string;
  temperatureC: { value: number };
  summary?: string;
}

export interface DeletedWeatherForecastState {
  aggregateType: 'DeletedWeatherForecast';
}

export type WeatherForecastPayloadUnion = WeatherForecastState | DeletedWeatherForecastState;

export const WeatherForecastStateSchema = z.object({
  aggregateType: z.literal('WeatherForecast'),
  location: z.string(),
  date: z.string(),
  temperatureC: TemperatureCelsiusSchema,
  summary: z.string().optional()
});

export const DeletedWeatherForecastStateSchema = z.object({
  aggregateType: z.literal('DeletedWeatherForecast')
});