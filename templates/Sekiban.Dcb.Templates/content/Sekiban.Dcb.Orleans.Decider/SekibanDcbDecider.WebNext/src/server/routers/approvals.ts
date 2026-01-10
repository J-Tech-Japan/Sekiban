import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";
import { createAuthHeaders } from "../lib/auth-helpers";
import { extractErrorMessage } from "../lib/api-error-helpers";

const approvalDecisionSchema = z.enum(["Approved", "Rejected"]);

const approvalInboxItemSchema = z.object({
  approvalRequestId: z.string().uuid(),
  reservationId: z.string().uuid(),
  roomId: z.string().uuid(),
  roomName: z.string().nullable().optional(),
  requesterId: z.string().uuid(),
  requestComment: z.string().nullable().optional(),
  organizerId: z.string().uuid().nullable().optional(),
  organizerName: z.string().nullable().optional(),
  purpose: z.string().nullable().optional(),
  startTime: z.string().nullable().optional(),
  endTime: z.string().nullable().optional(),
  approverIds: z.array(z.string().uuid()),
  requestedAt: z.string(),
  status: z.string(),
});

export const approvalsRouter = router({
  list: publicProcedure
    .input(
      z.object({
        pendingOnly: z.boolean().optional(),
        waitForSortableUniqueId: z.string().optional(),
        pageNumber: z.number().default(1),
        pageSize: z.number().default(100),
      })
    )
    .query(async ({ input }) => {
      const params = new URLSearchParams();
      params.set("pageNumber", input.pageNumber.toString());
      params.set("pageSize", input.pageSize.toString());
      if (input.waitForSortableUniqueId) {
        params.set("waitForSortableUniqueId", input.waitForSortableUniqueId);
      }
      if (input.pendingOnly !== undefined) {
        params.set("pendingOnly", input.pendingOnly ? "true" : "false");
      }

      const headers = await createAuthHeaders();
      const res = await fetch(`${process.env.API_BASE_URL}/api/approvals?${params.toString()}`, { headers });
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        if (res.status === 403) {
          throw new Error("Admin access required");
        }
        throw new Error("Failed to fetch approvals");
      }

      const data = await res.json();
      const items = z.array(approvalInboxItemSchema).parse(data);
      return input.pendingOnly ? items.filter((item) => item.status === "Pending") : items;
    }),

  decide: publicProcedure
    .input(
      z.object({
        approvalRequestId: z.string().uuid(),
        decision: approvalDecisionSchema,
        comment: z.string().optional(),
      })
    )
    .mutation(async ({ input }) => {
      const headers = await createAuthHeaders();
      const res = await fetch(
        `${process.env.API_BASE_URL}/api/approvals/${input.approvalRequestId}/decision`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({
            decision: input.decision,
            comment: input.comment,
          }),
        }
      );
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Authentication required");
        }
        if (res.status === 403) {
          throw new Error("Admin access required");
        }
        const error = await extractErrorMessage(res, "Failed to record approval decision");
        throw new Error(error);
      }
      return res.json();
    }),
});
