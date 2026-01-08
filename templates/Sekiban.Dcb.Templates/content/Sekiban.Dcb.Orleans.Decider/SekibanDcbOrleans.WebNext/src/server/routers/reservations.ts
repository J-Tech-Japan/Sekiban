import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";
import { createAuthHeaders } from "../lib/auth-helpers";

// Reservation status types matching the backend discriminated union
const reservationStatusSchema = z.enum([
  "Draft",
  "Held",
  "Confirmed",
  "Cancelled",
  "Rejected",
  "Expired",
]);

const reservationSchema = z.object({
  reservationId: z.string().uuid(),
  roomId: z.string().uuid(),
  organizerId: z.string().uuid(),
  startTime: z.string(),
  endTime: z.string(),
  purpose: z.string(),
  status: reservationStatusSchema,
  requiresApproval: z.boolean().optional(),
  approvalRequestId: z.string().uuid().optional().nullable(),
  confirmedAt: z.string().optional().nullable(),
  cancelledAt: z.string().optional().nullable(),
  reason: z.string().optional().nullable(),
});

const createDraftSchema = z.object({
  reservationId: z.string().uuid().optional(),
  roomId: z.string().uuid(),
  organizerId: z.string().uuid().optional(),
  startTime: z.string(),
  endTime: z.string(),
  purpose: z.string().min(1, "Purpose is required"),
});

const quickReservationSchema = z.object({
  roomId: z.string().uuid(),
  startTime: z.string(),
  endTime: z.string(),
  purpose: z.string().min(1, "Purpose is required"),
});

export const reservationsRouter = router({
  list: publicProcedure
    .input(
      z.object({
        pageNumber: z.number().default(1),
        pageSize: z.number().default(100),
        waitForSortableUniqueId: z.string().optional(),
        roomId: z.string().uuid().optional(),
      })
    )
    .query(async ({ input }) => {
      const params = new URLSearchParams();
      params.set("pageNumber", input.pageNumber.toString());
      params.set("pageSize", input.pageSize.toString());
      if (input.waitForSortableUniqueId) {
        params.set("waitForSortableUniqueId", input.waitForSortableUniqueId);
      }

      const url = input.roomId
        ? `${process.env.API_BASE_URL}/api/reservations/by-room/${input.roomId}?${params.toString()}`
        : `${process.env.API_BASE_URL}/api/reservations?${params.toString()}`;

      const headers = await createAuthHeaders();
      const res = await fetch(url, { headers });
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        throw new Error("Failed to fetch reservations");
      }
      const data = await res.json();
      return z.array(reservationSchema).parse(data);
    }),

  // Quick reservation (Draft → Hold → Confirm in one step)
  quickReserve: publicProcedure
    .input(quickReservationSchema)
    .mutation(async ({ input }) => {
      const headers = await createAuthHeaders();
      const res = await fetch(`${process.env.API_BASE_URL}/api/reservations/quick`, {
        method: "POST",
        headers,
        body: JSON.stringify({
          roomId: input.roomId,
          startTime: input.startTime,
          endTime: input.endTime,
          purpose: input.purpose,
        }),
      });
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        const error = await res.text();
        throw new Error(error || "Failed to create quick reservation");
      }
      return res.json();
    }),

  // Create draft
  createDraft: publicProcedure
    .input(createDraftSchema)
    .mutation(async ({ input }) => {
      const headers = await createAuthHeaders();
      const res = await fetch(`${process.env.API_BASE_URL}/api/reservations/draft`, {
        method: "POST",
        headers,
        body: JSON.stringify({
          reservationId: input.reservationId || crypto.randomUUID(),
          roomId: input.roomId,
          organizerId: input.organizerId || crypto.randomUUID(),
          startTime: input.startTime,
          endTime: input.endTime,
          purpose: input.purpose,
        }),
      });
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        const error = await res.text();
        throw new Error(error || "Failed to create reservation draft");
      }
      return res.json();
    }),

  // Commit hold
  commitHold: publicProcedure
    .input(
      z.object({
        reservationId: z.string().uuid(),
        roomId: z.string().uuid(),
        requiresApproval: z.boolean().default(false),
        approvalRequestId: z.string().uuid().optional(),
      })
    )
    .mutation(async ({ input }) => {
      const headers = await createAuthHeaders();
      const res = await fetch(
        `${process.env.API_BASE_URL}/api/reservations/${input.reservationId}/hold`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({
            roomId: input.roomId,
            requiresApproval: input.requiresApproval,
            approvalRequestId: input.approvalRequestId,
          }),
        }
      );
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        const error = await res.text();
        throw new Error(error || "Failed to commit hold");
      }
      return res.json();
    }),

  // Confirm reservation
  confirm: publicProcedure
    .input(
      z.object({
        reservationId: z.string().uuid(),
        roomId: z.string().uuid(),
      })
    )
    .mutation(async ({ input }) => {
      const headers = await createAuthHeaders();
      const res = await fetch(
        `${process.env.API_BASE_URL}/api/reservations/${input.reservationId}/confirm`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({
            roomId: input.roomId,
          }),
        }
      );
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        const error = await res.text();
        throw new Error(error || "Failed to confirm reservation");
      }
      return res.json();
    }),

  // Cancel reservation
  cancel: publicProcedure
    .input(
      z.object({
        reservationId: z.string().uuid(),
        roomId: z.string().uuid(),
        reason: z.string().min(1, "Reason is required"),
      })
    )
    .mutation(async ({ input }) => {
      const headers = await createAuthHeaders();
      const res = await fetch(
        `${process.env.API_BASE_URL}/api/reservations/${input.reservationId}/cancel`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({
            roomId: input.roomId,
            reason: input.reason,
          }),
        }
      );
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        const error = await res.text();
        throw new Error(error || "Failed to cancel reservation");
      }
      return res.json();
    }),
});
