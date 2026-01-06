import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";

const weatherForecastSchema = z.object({
  forecastId: z.string().uuid(),
  location: z.string(),
  date: z.string(),
  temperatureC: z.number(),
  summary: z.string().nullable(),
});

const createWeatherForecastSchema = z.object({
  forecastId: z.string().uuid().optional(),
  location: z.string().min(1, "Location is required"),
  date: z.string(),
  temperatureC: z.number().min(-60).max(60),
  summary: z.string().min(1, "Summary is required"),
});

const updateLocationSchema = z.object({
  forecastId: z.string().uuid(),
  newLocationName: z.string().min(1, "Location is required"),
});

export const weatherRouter = router({
  list: publicProcedure
    .input(
      z.object({
        pageNumber: z.number().default(1),
        pageSize: z.number().default(10),
        waitForSortableUniqueId: z.string().optional(),
      })
    )
    .query(async ({ input, ctx }) => {
      const params = new URLSearchParams();
      params.set("pageNumber", input.pageNumber.toString());
      params.set("pageSize", input.pageSize.toString());
      if (input.waitForSortableUniqueId) {
        params.set("waitForSortableUniqueId", input.waitForSortableUniqueId);
      }

      const res = await fetch(
        `${process.env.API_BASE_URL}/api/weatherforecast?${params.toString()}`
      );
      if (!res.ok) {
        throw new Error("Failed to fetch weather forecasts");
      }
      const data = await res.json();
      return z.array(weatherForecastSchema).parse(data);
    }),

  create: publicProcedure
    .input(createWeatherForecastSchema)
    .mutation(async ({ input }) => {
      const res = await fetch(
        `${process.env.API_BASE_URL}/api/inputweatherforecast`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            forecastId: input.forecastId || "00000000-0000-0000-0000-000000000000",
            location: input.location,
            date: input.date,
            temperatureC: input.temperatureC,
            summary: input.summary,
          }),
        }
      );
      if (!res.ok) {
        const error = await res.text();
        throw new Error(error || "Failed to create weather forecast");
      }
      return res.json();
    }),

  updateLocation: publicProcedure
    .input(updateLocationSchema)
    .mutation(async ({ input }) => {
      const res = await fetch(
        `${process.env.API_BASE_URL}/api/updateweatherforecastlocation`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            forecastId: input.forecastId,
            newLocationName: input.newLocationName,
          }),
        }
      );
      if (!res.ok) {
        const error = await res.text();
        throw new Error(error || "Failed to update location");
      }
      return res.json();
    }),

  remove: publicProcedure
    .input(z.object({ forecastId: z.string().uuid() }))
    .mutation(async ({ input }) => {
      const res = await fetch(
        `${process.env.API_BASE_URL}/api/removeweatherforecast`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ forecastId: input.forecastId }),
        }
      );
      if (!res.ok) {
        const error = await res.text();
        throw new Error(error || "Failed to remove weather forecast");
      }
      return res.json();
    }),
});
