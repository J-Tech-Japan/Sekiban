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

export default function ClassroomsPage() {
  const [pageSize, setPageSize] = useState(10);
  const [currentPage, setCurrentPage] = useState(1);
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<
    string | undefined
  >();

  // Add modal state
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [newClassName, setNewClassName] = useState("");
  const [newMaxCapacity, setNewMaxCapacity] = useState(20);
  const [formError, setFormError] = useState("");

  const {
    data: classrooms,
    isLoading,
    refetch,
  } = trpc.classrooms.list.useQuery({
    pageNumber: currentPage,
    pageSize,
    waitForSortableUniqueId: lastSortableUniqueId,
  });

  const createMutation = trpc.classrooms.create.useMutation({
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
    setNewClassName("");
    setNewMaxCapacity(20);
    setFormError("");
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newClassName) {
      setFormError("Classroom name is required");
      return;
    }
    createMutation.mutate({
      className: newClassName,
      maxCapacity: newMaxCapacity,
    });
  };

  if (isLoading) {
    return <p>Loading...</p>;
  }

  return (
    <div>
      <h1 className="text-3xl font-bold mb-4">Classroom Management</h1>

      <Button onClick={() => setIsAddModalOpen(true)} className="mb-4">
        Add New Classroom
      </Button>

      <h3 className="text-xl font-semibold mb-3">Classroom List</h3>

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
          Showing {classrooms?.length ?? 0} items
        </span>
      </div>

      {classrooms && classrooms.length > 0 ? (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Max Students</TableHead>
              <TableHead>Enrolled</TableHead>
              <TableHead>Available</TableHead>
              <TableHead>Status</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {classrooms.map((classroom) => (
              <TableRow key={classroom.classRoomId}>
                <TableCell>{classroom.name}</TableCell>
                <TableCell>{classroom.maxStudents}</TableCell>
                <TableCell>{classroom.enrolledCount}</TableCell>
                <TableCell>{classroom.remainingCapacity}</TableCell>
                <TableCell>
                  {classroom.isFull ? (
                    <span className="inline-flex items-center rounded-full bg-red-100 px-2.5 py-0.5 text-xs font-medium text-red-800">
                      Full
                    </span>
                  ) : (
                    <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">
                      Available
                    </span>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      ) : (
        <p className="text-muted-foreground">
          No classrooms available. Click &quot;Add New Classroom&quot; to create
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
            disabled={!classrooms || classrooms.length < pageSize}
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
                <CardTitle>Add New Classroom</CardTitle>
                <CardDescription>Create a new classroom</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div>
                  <label className="text-sm font-medium">Classroom Name</label>
                  <Input
                    value={newClassName}
                    onChange={(e) => setNewClassName(e.target.value)}
                    placeholder="Enter classroom name"
                  />
                </div>
                <div>
                  <label className="text-sm font-medium">Maximum Capacity</label>
                  <Input
                    type="number"
                    value={newMaxCapacity}
                    onChange={(e) => setNewMaxCapacity(Number(e.target.value))}
                    min={1}
                    max={100}
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
                  {createMutation.isPending ? "Adding..." : "Add Classroom"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}
    </div>
  );
}
