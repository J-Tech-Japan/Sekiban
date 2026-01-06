"use client";

import { useState } from "react";
import { trpc } from "@/lib/trpc";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
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

export default function StudentsPage() {
  const [pageSize, setPageSize] = useState(10);
  const [currentPage, setCurrentPage] = useState(1);
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<
    string | undefined
  >();

  // Add modal state
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [newName, setNewName] = useState("");
  const [newMaxClassCount, setNewMaxClassCount] = useState(5);
  const [formError, setFormError] = useState("");

  const { data: students, isLoading, refetch } = trpc.students.list.useQuery({
    pageNumber: currentPage,
    pageSize,
    waitForSortableUniqueId: lastSortableUniqueId,
  });

  const createMutation = trpc.students.create.useMutation({
    onSuccess: (data) => {
      setLastSortableUniqueId(data.sortableUniqueId);
      refetch();
      setIsAddModalOpen(false);
      resetForm();
    },
    onError: (error) => {
      setFormError(error.message);
    },
  });

  const resetForm = () => {
    setNewName("");
    setNewMaxClassCount(5);
    setFormError("");
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newName) {
      setFormError("Student name is required");
      return;
    }
    createMutation.mutate({
      name: newName,
      maxClassCount: newMaxClassCount,
    });
  };

  if (isLoading) {
    return <p>Loading...</p>;
  }

  return (
    <div>
      <h1 className="text-3xl font-bold mb-4">Student Management</h1>

      <Button onClick={() => setIsAddModalOpen(true)} className="mb-4">
        Add New Student
      </Button>

      <h3 className="text-xl font-semibold mb-3">Student List</h3>

      <div className="flex items-center gap-4 mb-4">
        <label className="text-sm">Page Size:</label>
        <Select
          value={pageSize.toString()}
          onChange={(e) => {
            setPageSize(Number(e.target.value));
            setCurrentPage(1);
          }}
          className="w-20"
        >
          <option value="5">5</option>
          <option value="10">10</option>
          <option value="20">20</option>
          <option value="50">50</option>
        </Select>
        <span className="text-sm text-muted-foreground">
          Showing {students?.length ?? 0} items
        </span>
      </div>

      {students && students.length > 0 ? (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Max Classes</TableHead>
              <TableHead>Enrolled Classes</TableHead>
              <TableHead>Available Slots</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {students.map((student) => (
              <TableRow key={student.studentId}>
                <TableCell>{student.name}</TableCell>
                <TableCell>{student.maxClassCount}</TableCell>
                <TableCell>{student.enrolledClassRoomIds.length}</TableCell>
                <TableCell>
                  {student.maxClassCount - student.enrolledClassRoomIds.length}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      ) : (
        <p className="text-muted-foreground">
          No students registered. Click &quot;Add New Student&quot; to create
          one.
        </p>
      )}

      <div className="flex justify-between items-center mt-4">
        <div className="flex gap-2">
          <Button
            variant="outline"
            onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
            disabled={currentPage === 1}
          >
            Previous
          </Button>
          <span className="px-4 py-2">Page {currentPage}</span>
          <Button
            variant="outline"
            onClick={() => setCurrentPage((p) => p + 1)}
            disabled={!students || students.length < pageSize}
          >
            Next
          </Button>
        </div>
      </div>

      {/* Add Modal */}
      {isAddModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <Card className="w-full max-w-md">
            <form onSubmit={handleSubmit}>
              <CardHeader>
                <CardTitle>Add New Student</CardTitle>
                <CardDescription>
                  Register a new student in the system
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div>
                  <label className="text-sm font-medium">Student Name</label>
                  <Input
                    value={newName}
                    onChange={(e) => setNewName(e.target.value)}
                    placeholder="Enter student name"
                  />
                </div>
                <div>
                  <label className="text-sm font-medium">Maximum Classes</label>
                  <Input
                    type="number"
                    value={newMaxClassCount}
                    onChange={(e) =>
                      setNewMaxClassCount(Number(e.target.value))
                    }
                    min={1}
                    max={10}
                  />
                </div>
                {formError && (
                  <p className="text-sm text-destructive">{formError}</p>
                )}
              </CardContent>
              <CardFooter className="flex justify-end gap-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => {
                    setIsAddModalOpen(false);
                    resetForm();
                  }}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={createMutation.isPending}>
                  {createMutation.isPending ? "Adding..." : "Add Student"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}
    </div>
  );
}
