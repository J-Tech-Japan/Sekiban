"use client";

import { useState } from "react";
import { trpc } from "@/lib/trpc";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";

export default function EnrollmentsPage() {
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<string | undefined>();

  // Selection state
  const [selectedStudentId, setSelectedStudentId] = useState("");
  const [selectedClassroomId, setSelectedClassroomId] = useState("");
  const [selectedEnrollmentForDrop, setSelectedEnrollmentForDrop] = useState("");

  // Messages
  const [enrollmentMessage, setEnrollmentMessage] = useState("");
  const [enrollmentError, setEnrollmentError] = useState(false);
  const [dropMessage, setDropMessage] = useState("");
  const [dropError, setDropError] = useState(false);

  const { data: students } = trpc.students.list.useQuery({
    pageNumber: 1,
    pageSize: 100,
    waitForSortableUniqueId: lastSortableUniqueId,
  });

  const { data: classrooms, refetch: refetchClassrooms } = trpc.classrooms.list.useQuery({
    pageNumber: 1,
    pageSize: 100,
    waitForSortableUniqueId: lastSortableUniqueId,
  });

  const { data: enrollments, isLoading, refetch: refetchEnrollments } = trpc.enrollments.list.useQuery({
    waitForSortableUniqueId: lastSortableUniqueId,
  });

  const enrollMutation = trpc.enrollments.enroll.useMutation({
    onSuccess: (data) => {
      setLastSortableUniqueId(data.sortableUniqueId);
      setEnrollmentMessage("Student enrolled successfully!");
      setEnrollmentError(false);
      setSelectedStudentId("");
      setSelectedClassroomId("");
      refetchEnrollments();
      refetchClassrooms();
    },
    onError: (error) => {
      setEnrollmentMessage(error.message || "Failed to enroll student");
      setEnrollmentError(true);
    },
  });

  const dropMutation = trpc.enrollments.drop.useMutation({
    onSuccess: (data) => {
      setLastSortableUniqueId(data.sortableUniqueId);
      setDropMessage("Student dropped successfully!");
      setDropError(false);
      setSelectedEnrollmentForDrop("");
      refetchEnrollments();
      refetchClassrooms();
    },
    onError: (error) => {
      setDropMessage(error.message || "Failed to drop student");
      setDropError(true);
    },
  });

  const handleEnrollStudent = () => {
    if (!selectedStudentId || !selectedClassroomId) {
      setEnrollmentMessage("Invalid selection. Please select both a student and a classroom.");
      setEnrollmentError(true);
      return;
    }
    enrollMutation.mutate({
      studentId: selectedStudentId,
      classRoomId: selectedClassroomId,
    });
  };

  const handleDropStudent = () => {
    if (!selectedEnrollmentForDrop) {
      setDropMessage("Please select an enrollment to drop.");
      setDropError(true);
      return;
    }
    const [studentId, classRoomId] = selectedEnrollmentForDrop.split(":");
    dropMutation.mutate({ studentId, classRoomId });
  };

  const handleQuickDrop = (studentId: string, classRoomId: string) => {
    dropMutation.mutate({ studentId, classRoomId });
  };

  const availableClassrooms = classrooms?.filter((c) => !c.isFull) ?? [];
  const totalEnrollments = enrollments?.length ?? 0;
  const totalStudents = students?.length ?? 0;
  const enrolledStudents = students?.filter(s => s.enrolledClassRoomIds.length > 0).length ?? 0;

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div>
        <h1 className="text-2xl font-semibold text-foreground">Enrollment Management</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Manage student enrollments in classrooms
        </p>
      </div>

      {/* Stats Cards */}
      <div className="grid gap-4 md:grid-cols-3">
        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-primary/10">
                <svg className="h-6 w-6 text-primary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{totalEnrollments}</p>
                <p className="text-sm text-muted-foreground">Active Enrollments</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-success/10">
                <svg className="h-6 w-6 text-success" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197m13.5-9a2.5 2.5 0 11-5 0 2.5 2.5 0 015 0z" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{enrolledStudents}/{totalStudents}</p>
                <p className="text-sm text-muted-foreground">Students Enrolled</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-warning/10">
                <svg className="h-6 w-6 text-warning" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{availableClassrooms.length}</p>
                <p className="text-sm text-muted-foreground">Available Classrooms</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Action Cards */}
      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <div className="flex items-center gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-success/10">
                <svg className="h-5 w-5 text-success" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M18 9v3m0 0v3m0-3h3m-3 0h-3m-2-5a4 4 0 11-8 0 4 4 0 018 0zM3 20a6 6 0 0112 0v1H3v-1z" />
                </svg>
              </div>
              <div>
                <CardTitle className="text-lg">Enroll Student</CardTitle>
                <CardDescription>Add a student to a classroom</CardDescription>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <label className="text-sm font-medium">Select Student</label>
              <Select value={selectedStudentId} onChange={(e) => setSelectedStudentId(e.target.value)}>
                <option value="">Choose a student...</option>
                {students?.map((student) => {
                  const canEnroll = student.enrolledClassRoomIds.length < student.maxClassCount;
                  return (
                    <option key={student.studentId} value={student.studentId} disabled={!canEnroll}>
                      {student.name} ({student.enrolledClassRoomIds.length}/{student.maxClassCount} classes)
                    </option>
                  );
                })}
              </Select>
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium">Select Classroom</label>
              <Select value={selectedClassroomId} onChange={(e) => setSelectedClassroomId(e.target.value)}>
                <option value="">Choose a classroom...</option>
                {availableClassrooms.map((classroom) => (
                  <option key={classroom.classRoomId} value={classroom.classRoomId}>
                    {classroom.name} ({classroom.remainingCapacity} spots left)
                  </option>
                ))}
              </Select>
            </div>

            <Button
              onClick={handleEnrollStudent}
              disabled={!selectedStudentId || !selectedClassroomId || enrollMutation.isPending}
              className="w-full"
            >
              {enrollMutation.isPending ? "Enrolling..." : "Enroll Student"}
            </Button>

            {enrollmentMessage && (
              <div className={`rounded-md p-3 text-sm ${enrollmentError ? "bg-destructive/10 text-destructive" : "bg-success/10 text-success"}`}>
                {enrollmentMessage}
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div className="flex items-center gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-destructive/10">
                <svg className="h-5 w-5 text-destructive" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7a4 4 0 11-8 0 4 4 0 018 0zM9 14a6 6 0 00-6 6v1h12v-1a6 6 0 00-6-6zM21 12h-6" />
                </svg>
              </div>
              <div>
                <CardTitle className="text-lg">Drop Student</CardTitle>
                <CardDescription>Remove a student from a classroom</CardDescription>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <label className="text-sm font-medium">Select Enrollment</label>
              <Select value={selectedEnrollmentForDrop} onChange={(e) => setSelectedEnrollmentForDrop(e.target.value)}>
                <option value="">Choose an enrollment...</option>
                {enrollments?.map((enrollment) => (
                  <option
                    key={`${enrollment.studentId}:${enrollment.classRoomId}`}
                    value={`${enrollment.studentId}:${enrollment.classRoomId}`}
                  >
                    {enrollment.studentName} - {enrollment.className}
                  </option>
                ))}
              </Select>
            </div>

            <Button
              variant="destructive"
              onClick={handleDropStudent}
              disabled={!selectedEnrollmentForDrop || dropMutation.isPending}
              className="w-full"
            >
              {dropMutation.isPending ? "Dropping..." : "Drop Student"}
            </Button>

            {dropMessage && (
              <div className={`rounded-md p-3 text-sm ${dropError ? "bg-destructive/10 text-destructive" : "bg-success/10 text-success"}`}>
                {dropMessage}
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Enrollments Table */}
      <Card>
        <CardHeader className="pb-4">
          <CardTitle className="text-lg">Current Enrollments</CardTitle>
          <CardDescription>View all active student enrollments</CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="flex items-center justify-center py-12">
              <div className="flex items-center gap-3 text-muted-foreground">
                <svg className="h-5 w-5 animate-spin" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                </svg>
                Loading enrollments...
              </div>
            </div>
          ) : enrollments && enrollments.length > 0 ? (
            <div className="border-t">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Student</TableHead>
                    <TableHead>Classroom</TableHead>
                    <TableHead>Enrollment Date</TableHead>
                    <TableHead className="text-center">Status</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {enrollments.map((enrollment) => (
                    <TableRow key={`${enrollment.studentId}:${enrollment.classRoomId}`}>
                      <TableCell>
                        <div className="flex items-center gap-3">
                          <div className="flex h-9 w-9 items-center justify-center rounded-full bg-primary/10 text-primary font-medium text-sm">
                            {enrollment.studentName.charAt(0).toUpperCase()}
                          </div>
                          <span className="font-medium">{enrollment.studentName}</span>
                        </div>
                      </TableCell>
                      <TableCell>
                        <div className="flex items-center gap-2">
                          <svg className="h-4 w-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
                          </svg>
                          <span>{enrollment.className}</span>
                        </div>
                      </TableCell>
                      <TableCell className="text-muted-foreground">
                        {new Date(enrollment.enrollmentDate).toLocaleDateString(undefined, {
                          year: 'numeric',
                          month: 'short',
                          day: 'numeric'
                        })}
                      </TableCell>
                      <TableCell className="text-center">
                        <Badge variant="success">Active</Badge>
                      </TableCell>
                      <TableCell className="text-right">
                        <Button
                          variant="destructive"
                          size="sm"
                          onClick={() => handleQuickDrop(enrollment.studentId, enrollment.classRoomId)}
                        >
                          Drop
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <div className="flex h-16 w-16 items-center justify-center rounded-full bg-muted mb-4">
                <svg className="h-8 w-8 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4" />
                </svg>
              </div>
              <h3 className="text-lg font-medium mb-1">No enrollments yet</h3>
              <p className="text-sm text-muted-foreground mb-4">Use the form above to enroll students in classrooms</p>
            </div>
          )}
        </CardContent>
        {enrollments && enrollments.length > 0 && (
          <CardFooter className="border-t py-4">
            <p className="text-sm text-muted-foreground">Showing {enrollments.length} enrollments</p>
          </CardFooter>
        )}
      </Card>
    </div>
  );
}
