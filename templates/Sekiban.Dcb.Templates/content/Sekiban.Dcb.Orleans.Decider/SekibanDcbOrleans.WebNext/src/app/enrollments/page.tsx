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
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export default function EnrollmentsPage() {
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<
    string | undefined
  >();

  // Selection state
  const [selectedStudentId, setSelectedStudentId] = useState("");
  const [selectedClassroomId, setSelectedClassroomId] = useState("");
  const [selectedEnrollmentForDrop, setSelectedEnrollmentForDrop] =
    useState("");

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

  const { data: classrooms, refetch: refetchClassrooms } =
    trpc.classrooms.list.useQuery({
      pageNumber: 1,
      pageSize: 100,
      waitForSortableUniqueId: lastSortableUniqueId,
    });

  const { data: enrollments, refetch: refetchEnrollments } =
    trpc.enrollments.list.useQuery({
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
      setEnrollmentMessage(
        "Invalid selection. Please select both a student and a classroom."
      );
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

  return (
    <div>
      <h1 className="text-3xl font-bold mb-4">Enrollment Management</h1>

      <div className="grid gap-4 md:grid-cols-2 mb-6">
        <Card>
          <CardHeader>
            <CardTitle>Enroll Student in Classroom</CardTitle>
            <CardDescription>
              Select a student and classroom to enroll
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div>
              <label className="text-sm font-medium">Select Student</label>
              <Select
                value={selectedStudentId}
                onChange={(e) => setSelectedStudentId(e.target.value)}
              >
                <option value="">-- Select a Student --</option>
                {students?.map((student) => (
                  <option key={student.studentId} value={student.studentId}>
                    {student.name} (Max: {student.maxClassCount})
                  </option>
                ))}
              </Select>
            </div>

            <div>
              <label className="text-sm font-medium">Select Classroom</label>
              <Select
                value={selectedClassroomId}
                onChange={(e) => setSelectedClassroomId(e.target.value)}
              >
                <option value="">-- Select a Classroom --</option>
                {availableClassrooms.map((classroom) => (
                  <option
                    key={classroom.classRoomId}
                    value={classroom.classRoomId}
                  >
                    {classroom.name} (Available: {classroom.remainingCapacity}/
                    {classroom.maxStudents})
                  </option>
                ))}
              </Select>
            </div>

            <Button
              onClick={handleEnrollStudent}
              disabled={
                !selectedStudentId ||
                !selectedClassroomId ||
                enrollMutation.isPending
              }
            >
              {enrollMutation.isPending ? "Enrolling..." : "Enroll Student"}
            </Button>

            {enrollmentMessage && (
              <div
                className={`p-3 rounded-md text-sm ${
                  enrollmentError
                    ? "bg-red-100 text-red-800"
                    : "bg-green-100 text-green-800"
                }`}
              >
                {enrollmentMessage}
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Drop Student from Classroom</CardTitle>
            <CardDescription>
              Select an enrollment to remove a student
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div>
              <label className="text-sm font-medium">
                Select Enrolled Student
              </label>
              <Select
                value={selectedEnrollmentForDrop}
                onChange={(e) => setSelectedEnrollmentForDrop(e.target.value)}
              >
                <option value="">-- Select an Enrollment --</option>
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
              variant="secondary"
              onClick={handleDropStudent}
              disabled={!selectedEnrollmentForDrop || dropMutation.isPending}
            >
              {dropMutation.isPending ? "Dropping..." : "Drop Student"}
            </Button>

            {dropMessage && (
              <div
                className={`p-3 rounded-md text-sm ${
                  dropError
                    ? "bg-red-100 text-red-800"
                    : "bg-green-100 text-green-800"
                }`}
              >
                {dropMessage}
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      <div className="mt-6">
        <h3 className="text-xl font-semibold mb-3">Current Enrollments</h3>

        {enrollments && enrollments.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Student Name</TableHead>
                <TableHead>Classroom</TableHead>
                <TableHead>Enrollment Date</TableHead>
                <TableHead>Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {enrollments.map((enrollment) => (
                <TableRow
                  key={`${enrollment.studentId}:${enrollment.classRoomId}`}
                >
                  <TableCell>{enrollment.studentName}</TableCell>
                  <TableCell>{enrollment.className}</TableCell>
                  <TableCell>
                    {new Date(enrollment.enrollmentDate).toLocaleDateString()}
                  </TableCell>
                  <TableCell>
                    <Button
                      variant="destructive"
                      size="sm"
                      onClick={() =>
                        handleQuickDrop(
                          enrollment.studentId,
                          enrollment.classRoomId
                        )
                      }
                    >
                      Drop
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p className="text-muted-foreground">No enrollments found.</p>
        )}
      </div>
    </div>
  );
}
