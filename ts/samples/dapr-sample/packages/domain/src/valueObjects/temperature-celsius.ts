import { z } from 'zod';

export const TemperatureCelsiusSchema = z.object({
  value: z.number().refine(
    (val) => val >= -273,
    { message: "Temperature cannot be below absolute zero." }
  )
});

export type TemperatureCelsius = z.infer<typeof TemperatureCelsiusSchema>;

export const createTemperatureCelsius = (value: number): TemperatureCelsius => {
  return TemperatureCelsiusSchema.parse({ value });
};

export const toFahrenheit = (temp: TemperatureCelsius): number => {
  return 32 + Math.floor(temp.value / 0.5556);
};