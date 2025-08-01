import { z } from 'zod';
import { defineEvent } from '@sekiban/core';
import { TemperatureCelsiusSchema } from '../../../valueObjects/temperature-celsius.js';

export const WeatherForecastInputted = defineEvent({
  type: 'WeatherForecastInputted',
  schema: z.object({
    location: z.string(),
    date: z.string(),
    temperatureC: TemperatureCelsiusSchema,
    summary: z.string().optional()
  })
});

export const WeatherForecastLocationUpdated = defineEvent({
  type: 'WeatherForecastLocationUpdated',
  schema: z.object({
    location: z.string()
  })
});

export const WeatherForecastDeleted = defineEvent({
  type: 'WeatherForecastDeleted',
  schema: z.object({})
});