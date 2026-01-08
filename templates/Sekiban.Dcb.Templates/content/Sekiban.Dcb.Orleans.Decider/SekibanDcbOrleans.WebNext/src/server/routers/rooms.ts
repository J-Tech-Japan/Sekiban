import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";
import { createAuthHeaders } from "../lib/auth-helpers";
import { extractErrorMessage } from "../lib/api-error-helpers";

const roomSchema = z.object({
  roomId: z.string().uuid(),
  name: z.string(),
  capacity: z.number(),
  location: z.string(),
  equipment: z.array(z.string()),
  requiresApproval: z.boolean().optional().default(false),
  isActive: z.boolean().optional().default(true),
});

const createRoomSchema = z.object({
  roomId: z.string().uuid().optional(),
  name: z.string().min(1, "Room name is required"),
  capacity: z.number().min(1, "Capacity must be at least 1"),
  location: z.string().min(1, "Location is required"),
  equipment: z.array(z.string()).default([]),
  requiresApproval: z.boolean().optional().default(false),
});

const updateRoomSchema = z.object({
  roomId: z.string().uuid(),
  name: z.string().min(1, "Room name is required"),
  capacity: z.number().min(1, "Capacity must be at least 1"),
  location: z.string().min(1, "Location is required"),
  equipment: z.array(z.string()).default([]),
  requiresApproval: z.boolean().optional().default(false),
});

export const roomsRouter = router({
  list: publicProcedure
    .input(
      z.object({
        pageNumber: z.number().default(1),
        pageSize: z.number().default(100),
        waitForSortableUniqueId: z.string().optional(),
      })
    )
    .query(async ({ input }) => {
      const params = new URLSearchParams();
      params.set("pageNumber", input.pageNumber.toString());
      params.set("pageSize", input.pageSize.toString());
      if (input.waitForSortableUniqueId) {
        params.set("waitForSortableUniqueId", input.waitForSortableUniqueId);
      }

      const headers = await createAuthHeaders();
      const res = await fetch(
        `${process.env.API_BASE_URL}/api/rooms?${params.toString()}`,
        { headers }
      );
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        throw new Error("Failed to fetch rooms");
      }
      const data = await res.json();
      return z.array(roomSchema).parse(data);
    }),

  create: publicProcedure
    .input(createRoomSchema)
    .mutation(async ({ input }) => {
      const headers = await createAuthHeaders();
      const res = await fetch(`${process.env.API_BASE_URL}/api/rooms`, {
        method: "POST",
        headers,
        body: JSON.stringify({
          roomId: input.roomId || crypto.randomUUID(),
          name: input.name,
          capacity: input.capacity,
          location: input.location,
          equipment: input.equipment,
          requiresApproval: input.requiresApproval,
        }),
      });
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        const error = await extractErrorMessage(res, "Failed to create room");
        throw new Error(error);
      }
      return res.json();
    }),

  update: publicProcedure
    .input(updateRoomSchema)
    .mutation(async ({ input }) => {
      const headers = await createAuthHeaders();
      const res = await fetch(
        `${process.env.API_BASE_URL}/api/rooms/${input.roomId}`,
        {
          method: "PUT",
          headers,
          body: JSON.stringify({
            name: input.name,
            capacity: input.capacity,
            location: input.location,
            equipment: input.equipment,
            requiresApproval: input.requiresApproval,
          }),
        }
      );
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        const error = await extractErrorMessage(res, "Failed to update room");
        throw new Error(error);
      }
      return res.json();
    }),
});
