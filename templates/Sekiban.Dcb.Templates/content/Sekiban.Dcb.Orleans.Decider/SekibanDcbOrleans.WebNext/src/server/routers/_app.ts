import { router } from "../api/trpc";
import { weatherRouter } from "./weather";
import { studentsRouter } from "./students";
import { classroomsRouter } from "./classrooms";
import { enrollmentsRouter } from "./enrollments";
import { authRouter } from "./auth";
import { roomsRouter } from "./rooms";
import { reservationsRouter } from "./reservations";
import { approvalsRouter } from "./approvals";
import { testDataRouter } from "./testData";
import { usersRouter } from "./users";

export const appRouter = router({
  weather: weatherRouter,
  students: studentsRouter,
  classrooms: classroomsRouter,
  enrollments: enrollmentsRouter,
  auth: authRouter,
  rooms: roomsRouter,
  reservations: reservationsRouter,
  approvals: approvalsRouter,
  testData: testDataRouter,
  users: usersRouter,
});

export type AppRouter = typeof appRouter;
