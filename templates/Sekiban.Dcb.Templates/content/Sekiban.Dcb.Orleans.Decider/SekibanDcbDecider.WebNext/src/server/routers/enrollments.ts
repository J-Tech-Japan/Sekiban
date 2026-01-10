import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";

const enrollmentSchema = z.object({
  studentId: z.string().uuid(),
  studentName: z.string(),
  classRoomId: z.string().uuid(),
  className: z.string(),
  enrollmentDate: z.string(),
});

const enrollStudentSchema = z.object({
  studentId: z.string().uuid(),
  classRoomId: z.string().uuid(),
});

const dropStudentSchema = z.object({
  studentId: z.string().uuid(),
  classRoomId: z.string().uuid(),
});

export const enrollmentsRouter = router({
  list: publicProcedure
    .input(
      z.object({
        waitForSortableUniqueId: z.string().optional(),
      })
    )
    .query(async ({ input }) => {
      // Build enrollments from students and classrooms data
      const params = new URLSearchParams();
      if (input.waitForSortableUniqueId) {
        params.set("waitForSortableUniqueId", input.waitForSortableUniqueId);
      }

      const [studentsRes, classroomsRes] = await Promise.all([
        fetch(`${process.env.API_BASE_URL}/api/students?${params.toString()}`),
        fetch(`${process.env.API_BASE_URL}/api/classrooms?${params.toString()}`),
      ]);

      if (!studentsRes.ok || !classroomsRes.ok) {
        throw new Error("Failed to fetch enrollment data");
      }

      const students = await studentsRes.json();
      const classrooms = await classroomsRes.json();

      const enrollments: z.infer<typeof enrollmentSchema>[] = [];

      for (const student of students) {
        for (const classRoomId of student.enrolledClassRoomIds || []) {
          const classroom = classrooms.find(
            (c: { classRoomId: string }) => c.classRoomId === classRoomId
          );
          if (classroom) {
            enrollments.push({
              studentId: student.studentId,
              studentName: student.name,
              classRoomId: classroom.classRoomId,
              className: classroom.name,
              enrollmentDate: new Date().toISOString(),
            });
          }
        }
      }

      return enrollments;
    }),

  enroll: publicProcedure
    .input(enrollStudentSchema)
    .mutation(async ({ input }) => {
      const res = await fetch(`${process.env.API_BASE_URL}/api/enrollments`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          studentId: input.studentId,
          classRoomId: input.classRoomId,
        }),
      });
      if (!res.ok) {
        const error = await res.text();
        throw new Error(error || "Failed to enroll student");
      }
      return res.json();
    }),

  drop: publicProcedure.input(dropStudentSchema).mutation(async ({ input }) => {
    const res = await fetch(
      `${process.env.API_BASE_URL}/api/enrollments/drop`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          studentId: input.studentId,
          classRoomId: input.classRoomId,
        }),
      }
    );
    if (!res.ok) {
      const error = await res.text();
      throw new Error(error || "Failed to drop student");
    }
    return res.json();
  }),
});
