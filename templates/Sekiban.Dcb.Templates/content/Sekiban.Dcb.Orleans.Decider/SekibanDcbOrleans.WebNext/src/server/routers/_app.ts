import { router } from "../api/trpc";
import { weatherRouter } from "./weather";
import { studentsRouter } from "./students";
import { classroomsRouter } from "./classrooms";
import { enrollmentsRouter } from "./enrollments";

export const appRouter = router({
  weather: weatherRouter,
  students: studentsRouter,
  classrooms: classroomsRouter,
  enrollments: enrollmentsRouter,
});

export type AppRouter = typeof appRouter;
