import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";

const classroomSchema = z.object({
  classRoomId: z.string().uuid(),
  name: z.string(),
  maxStudents: z.number(),
  enrolledCount: z.number(),
  remainingCapacity: z.number(),
  isFull: z.boolean(),
});

const createClassroomSchema = z.object({
  className: z.string().min(1, "Classroom name is required"),
  maxCapacity: z.number().min(1).max(100).default(20),
});

export const classroomsRouter = router({
  list: publicProcedure
    .input(
      z.object({
        pageNumber: z.number().default(1),
        pageSize: z.number().default(10),
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

      const res = await fetch(
        `${process.env.API_BASE_URL}/api/classrooms?${params.toString()}`
      );
      if (!res.ok) {
        throw new Error("Failed to fetch classrooms");
      }
      const data = await res.json();
      return z.array(classroomSchema).parse(data);
    }),

  create: publicProcedure
    .input(createClassroomSchema)
    .mutation(async ({ input }) => {
      const classRoomId = crypto.randomUUID();
      const res = await fetch(`${process.env.API_BASE_URL}/api/classrooms`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          classRoomId,
          name: input.className,
          maxStudents: input.maxCapacity,
        }),
      });
      if (!res.ok) {
        const error = await res.text();
        throw new Error(error || "Failed to create classroom");
      }
      return res.json();
    }),
});
