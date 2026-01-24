import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";
import { createAuthHeaders } from "../lib/auth-helpers";
import { extractErrorMessage } from "../lib/api-error-helpers";

const userDirectoryItemSchema = z.object({
  userId: z.string().uuid(),
  displayName: z.string(),
  email: z.string(),
  department: z.string().nullable().optional(),
  isActive: z.boolean(),
  registeredAt: z.string(),
  monthlyReservationLimit: z.number(),
  externalProviders: z.array(z.string()),
  roles: z.array(z.string()),
});

export const usersRouter = router({
  list: publicProcedure
    .input(
      z.object({
        pageNumber: z.number().default(1),
        pageSize: z.number().default(100),
        activeOnly: z.boolean().optional(),
        waitForSortableUniqueId: z.string().optional(),
      })
    )
    .query(async ({ input }) => {
      const params = new URLSearchParams();
      params.set("pageNumber", input.pageNumber.toString());
      params.set("pageSize", input.pageSize.toString());
      if (input.activeOnly !== undefined) {
        params.set("activeOnly", input.activeOnly ? "true" : "false");
      }
      if (input.waitForSortableUniqueId) {
        params.set("waitForSortableUniqueId", input.waitForSortableUniqueId);
      }

      const headers = await createAuthHeaders();
      const res = await fetch(`${process.env.API_BASE_URL}/api/users?${params.toString()}`, { headers });
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        if (res.status === 403) {
          throw new Error("Admin access required");
        }
        throw new Error("Failed to fetch users");
      }
      const data = await res.json();
      return z.array(userDirectoryItemSchema).parse(data);
    }),

  updateMonthlyLimit: publicProcedure
    .input(
      z.object({
        userId: z.string().uuid(),
        monthlyReservationLimit: z.number().int().min(1),
      })
    )
    .mutation(async ({ input }) => {
      const headers = await createAuthHeaders();
      const res = await fetch(`${process.env.API_BASE_URL}/api/users/${input.userId}/monthly-limit`, {
        method: "POST",
        headers,
        body: JSON.stringify({
          monthlyReservationLimit: input.monthlyReservationLimit,
        }),
      });
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        if (res.status === 403) {
          throw new Error("Admin access required");
        }
        const error = await extractErrorMessage(res, "Failed to update monthly limit");
        throw new Error(error);
      }
      return res.json();
    }),
});
