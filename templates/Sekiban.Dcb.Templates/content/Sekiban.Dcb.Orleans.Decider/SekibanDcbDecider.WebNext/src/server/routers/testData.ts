import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";
import { createAuthHeaders } from "../lib/auth-helpers";

const testDataResultSchema = z.object({
  roomsCreated: z.number(),
  roomIds: z.array(z.string().uuid()),
  reservationsCreated: z.number(),
  reservationIds: z.array(z.string().uuid()),
});
const timeZoneOffsetSchema = z.object({
  timeZoneOffsetMinutes: z.number().int().optional(),
});

export const testDataRouter = router({
  generate: publicProcedure.input(timeZoneOffsetSchema.optional()).mutation(async ({ input }) => {
    const headers = await createAuthHeaders();
    const params = new URLSearchParams();
    if (input?.timeZoneOffsetMinutes !== undefined) {
      params.set("timeZoneOffsetMinutes", input.timeZoneOffsetMinutes.toString());
    }
    const url = `${process.env.API_BASE_URL}/api/test-data/generate${params.toString() ? `?${params.toString()}` : ""}`;
    const res = await fetch(url, {
      method: "POST",
      headers,
    });
    if (!res.ok) {
      if (res.status === 401) {
        throw new Error("Authentication required");
      }
      const error = await res.text();
      throw new Error(error || "Failed to generate test data");
    }
    return testDataResultSchema.parse(await res.json());
  }),

  generateRooms: publicProcedure.mutation(async () => {
    const headers = await createAuthHeaders();
    const res = await fetch(`${process.env.API_BASE_URL}/api/test-data/generate-rooms`, {
      method: "POST",
      headers,
    });
    if (!res.ok) {
      if (res.status === 401) {
        throw new Error("Authentication required");
      }
      const error = await res.text();
      throw new Error(error || "Failed to generate rooms");
    }
    return res.json();
  }),

  generateReservations: publicProcedure
    .input(z.object({ roomId: z.string().uuid().optional(), timeZoneOffsetMinutes: z.number().int().optional() }))
    .mutation(async ({ input }) => {
      const params = new URLSearchParams();
      if (input.roomId) {
        params.set("roomId", input.roomId);
      }
      if (input.timeZoneOffsetMinutes !== undefined) {
        params.set("timeZoneOffsetMinutes", input.timeZoneOffsetMinutes.toString());
      }
      const url = `${process.env.API_BASE_URL}/api/test-data/generate-reservations${params.toString() ? `?${params.toString()}` : ""}`;
      const headers = await createAuthHeaders();
      const res = await fetch(url, {
        method: "POST",
        headers,
      });
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        const error = await res.text();
        throw new Error(error || "Failed to generate reservations");
      }
      return res.json();
    }),
});
