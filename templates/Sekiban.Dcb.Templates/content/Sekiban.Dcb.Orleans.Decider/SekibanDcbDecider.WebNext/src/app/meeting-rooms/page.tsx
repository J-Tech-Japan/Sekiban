"use client";

import { useEffect, useState } from "react";
import { trpc } from "@/lib/trpc";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
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
import { RequireAuth } from "@/components/auth/require-auth";

export default function MeetingRoomsPage() {
  return (
    <RequireAuth>
      <MeetingRoomsContent />
    </RequireAuth>
  );
}

function MeetingRoomsContent() {
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<string | undefined>();

  // Modal state
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);

  // Form state
  const [formData, setFormData] = useState({
    roomId: "",
    name: "",
    capacity: 10,
    location: "",
    equipment: [] as string[],
    requiresApproval: false,
  });
  const [equipmentInput, setEquipmentInput] = useState("");
  const [formError, setFormError] = useState("");

  const { data: rooms, isLoading, refetch } = trpc.rooms.list.useQuery({
    waitForSortableUniqueId: lastSortableUniqueId,
  });

  const createMutation = trpc.rooms.create.useMutation({
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

  const updateMutation = trpc.rooms.update.useMutation({
    onSuccess: (data) => {
      setLastSortableUniqueId(data.sortableUniqueId);
      refetch();
      setIsEditModalOpen(false);
      resetForm();
    },
    onError: (error) => {
      setFormError(error.message);
    },
  });

  const resetForm = () => {
    setFormData({
      roomId: "",
      name: "",
      capacity: 10,
      location: "",
      equipment: [],
      requiresApproval: false,
    });
    setEquipmentInput("");
    setFormError("");
  };

  useEffect(() => {
    if (!isAddModalOpen && !isEditModalOpen) return;

    const handleKey = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;

      if (isEditModalOpen) {
        setIsEditModalOpen(false);
        resetForm();
        return;
      }

      if (isAddModalOpen) {
        setIsAddModalOpen(false);
        resetForm();
      }
    };

    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [isAddModalOpen, isEditModalOpen, resetForm]);

  const handleAddSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.name || !formData.location) {
      setFormError("Please fill in all required fields");
      return;
    }
    createMutation.mutate({
      name: formData.name,
      capacity: formData.capacity,
      location: formData.location,
      equipment: formData.equipment,
      requiresApproval: formData.requiresApproval,
    });
  };

  const handleEditSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.name || !formData.location) {
      setFormError("Please fill in all required fields");
      return;
    }
    updateMutation.mutate({
      roomId: formData.roomId,
      name: formData.name,
      capacity: formData.capacity,
      location: formData.location,
      equipment: formData.equipment,
      requiresApproval: formData.requiresApproval,
    });
  };

  const openEditModal = (room: typeof rooms extends (infer T)[] | undefined ? T : never) => {
    if (!room) return;
    setFormData({
      roomId: room.roomId,
      name: room.name,
      capacity: room.capacity,
      location: room.location,
      equipment: room.equipment,
      requiresApproval: room.requiresApproval ?? false,
    });
    setFormError("");
    setIsEditModalOpen(true);
  };

  const addEquipment = () => {
    if (equipmentInput.trim() && !formData.equipment.includes(equipmentInput.trim())) {
      setFormData({
        ...formData,
        equipment: [...formData.equipment, equipmentInput.trim()],
      });
      setEquipmentInput("");
    }
  };

  const removeEquipment = (item: string) => {
    setFormData({
      ...formData,
      equipment: formData.equipment.filter((e) => e !== item),
    });
  };

  const totalRooms = rooms?.length ?? 0;
  const activeRooms = rooms?.filter((r) => r.isActive).length ?? 0;
  const totalCapacity = rooms?.reduce((sum, r) => sum + r.capacity, 0) ?? 0;

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-foreground">Meeting Rooms</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Manage meeting rooms and their equipment
          </p>
        </div>
        <Button onClick={() => setIsAddModalOpen(true)} className="gap-2">
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          Add Room
        </Button>
      </div>

      {/* Stats Cards */}
      <div className="grid gap-4 md:grid-cols-3">
        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-primary/10">
                <svg className="h-6 w-6 text-primary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{totalRooms}</p>
                <p className="text-sm text-muted-foreground">Total Rooms</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-success/10">
                <svg className="h-6 w-6 text-success" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{activeRooms}</p>
                <p className="text-sm text-muted-foreground">Active Rooms</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-warning/10">
                <svg className="h-6 w-6 text-warning" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{totalCapacity}</p>
                <p className="text-sm text-muted-foreground">Total Capacity</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Rooms Table */}
      <Card>
        <CardHeader className="pb-4">
          <CardTitle className="text-lg">Room List</CardTitle>
          <CardDescription>View and manage meeting room configurations</CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="flex items-center justify-center py-12">
              <div className="flex items-center gap-3 text-muted-foreground">
                <svg className="h-5 w-5 animate-spin" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                </svg>
                Loading rooms...
              </div>
            </div>
          ) : rooms && rooms.length > 0 ? (
            <div className="border-t">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Room Name</TableHead>
                    <TableHead>Location</TableHead>
                    <TableHead className="text-center">Capacity</TableHead>
                    <TableHead>Equipment</TableHead>
                    <TableHead className="text-center">Approval</TableHead>
                    <TableHead className="text-center">Status</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rooms.map((room) => (
                    <TableRow key={room.roomId}>
                      <TableCell>
                        <div className="flex items-center gap-3">
                          <div className="flex h-9 w-9 items-center justify-center rounded-full bg-primary/10 text-primary">
                            <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5" />
                            </svg>
                          </div>
                          <span className="font-medium">{room.name}</span>
                        </div>
                      </TableCell>
                      <TableCell className="text-muted-foreground">{room.location}</TableCell>
                      <TableCell className="text-center">
                        <div className="flex items-center justify-center gap-1">
                          <svg className="h-4 w-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z" />
                          </svg>
                          <span className="font-medium">{room.capacity}</span>
                        </div>
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          {room.equipment.length > 0 ? (
                            room.equipment.slice(0, 3).map((eq) => (
                              <Badge key={eq} variant="secondary" className="text-xs">
                                {eq}
                              </Badge>
                            ))
                          ) : (
                            <span className="text-muted-foreground text-sm">No equipment</span>
                          )}
                          {room.equipment.length > 3 && (
                            <Badge variant="secondary" className="text-xs">
                              +{room.equipment.length - 3}
                            </Badge>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="text-center">
                        <Badge variant={room.requiresApproval ? "warning" : "secondary"}>
                          {room.requiresApproval ? "Required" : "Auto"}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-center">
                        <Badge variant={room.isActive ? "success" : "secondary"}>
                          {room.isActive ? "Active" : "Inactive"}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-right">
                        <Button variant="outline" size="sm" onClick={() => openEditModal(room)}>
                          Edit
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
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5" />
                </svg>
              </div>
              <h3 className="text-lg font-medium mb-1">No rooms yet</h3>
              <p className="text-sm text-muted-foreground mb-4">Get started by adding your first meeting room</p>
              <Button onClick={() => setIsAddModalOpen(true)}>Add Room</Button>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Add Modal */}
      {isAddModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md animate-in fade-in zoom-in-95 duration-200">
            <form onSubmit={handleAddSubmit}>
              <CardHeader>
                <CardTitle>Add Meeting Room</CardTitle>
                <CardDescription>Create a new meeting room</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">Room Name *</label>
                  <Input
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="e.g., Conference Room A"
                    autoFocus
                  />
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Location *</label>
                  <Input
                    value={formData.location}
                    onChange={(e) => setFormData({ ...formData, location: e.target.value })}
                    placeholder="e.g., Building 1, Floor 3"
                  />
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Capacity</label>
                  <Input
                    type="number"
                    value={formData.capacity}
                    onChange={(e) => setFormData({ ...formData, capacity: Number(e.target.value) })}
                    min={1}
                    max={500}
                  />
                </div>
                <div className="flex items-center gap-2">
                  <input
                    id="requires-approval"
                    type="checkbox"
                    className="h-4 w-4 rounded border-muted-foreground"
                    checked={formData.requiresApproval}
                    onChange={(e) => setFormData({ ...formData, requiresApproval: e.target.checked })}
                  />
                  <label htmlFor="requires-approval" className="text-sm font-medium">
                    Requires approval
                  </label>
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Equipment</label>
                  <div className="flex gap-2">
                    <Input
                      value={equipmentInput}
                      onChange={(e) => setEquipmentInput(e.target.value)}
                      placeholder="e.g., Projector"
                      onKeyDown={(e) => {
                        if (e.key === "Enter") {
                          e.preventDefault();
                          addEquipment();
                        }
                      }}
                    />
                    <Button type="button" variant="outline" onClick={addEquipment}>
                      Add
                    </Button>
                  </div>
                  <div className="flex flex-wrap gap-2 mt-2">
                    {formData.equipment.map((eq) => (
                      <Badge key={eq} variant="secondary" className="gap-1">
                        {eq}
                        <button
                          type="button"
                          onClick={() => removeEquipment(eq)}
                          className="ml-1 hover:text-destructive"
                        >
                          ×
                        </button>
                      </Badge>
                    ))}
                  </div>
                </div>
                {formError && (
                  <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{formError}</div>
                )}
              </CardContent>
              <CardFooter className="flex justify-end gap-2 border-t pt-4">
                <Button type="button" variant="outline" onClick={() => { setIsAddModalOpen(false); resetForm(); }}>
                  Cancel
                </Button>
                <Button type="submit" disabled={createMutation.isPending}>
                  {createMutation.isPending ? "Adding..." : "Add Room"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* Edit Modal */}
      {isEditModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md animate-in fade-in zoom-in-95 duration-200">
            <form onSubmit={handleEditSubmit}>
              <CardHeader>
                <CardTitle>Edit Meeting Room</CardTitle>
                <CardDescription>Update room configuration</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">Room Name *</label>
                  <Input
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="e.g., Conference Room A"
                    autoFocus
                  />
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Location *</label>
                  <Input
                    value={formData.location}
                    onChange={(e) => setFormData({ ...formData, location: e.target.value })}
                    placeholder="e.g., Building 1, Floor 3"
                  />
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Capacity</label>
                  <Input
                    type="number"
                    value={formData.capacity}
                    onChange={(e) => setFormData({ ...formData, capacity: Number(e.target.value) })}
                    min={1}
                    max={500}
                  />
                </div>
                <div className="flex items-center gap-2">
                  <input
                    id="requires-approval-edit"
                    type="checkbox"
                    className="h-4 w-4 rounded border-muted-foreground"
                    checked={formData.requiresApproval}
                    onChange={(e) => setFormData({ ...formData, requiresApproval: e.target.checked })}
                  />
                  <label htmlFor="requires-approval-edit" className="text-sm font-medium">
                    Requires approval
                  </label>
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Equipment</label>
                  <div className="flex gap-2">
                    <Input
                      value={equipmentInput}
                      onChange={(e) => setEquipmentInput(e.target.value)}
                      placeholder="e.g., Projector"
                      onKeyDown={(e) => {
                        if (e.key === "Enter") {
                          e.preventDefault();
                          addEquipment();
                        }
                      }}
                    />
                    <Button type="button" variant="outline" onClick={addEquipment}>
                      Add
                    </Button>
                  </div>
                  <div className="flex flex-wrap gap-2 mt-2">
                    {formData.equipment.map((eq) => (
                      <Badge key={eq} variant="secondary" className="gap-1">
                        {eq}
                        <button
                          type="button"
                          onClick={() => removeEquipment(eq)}
                          className="ml-1 hover:text-destructive"
                        >
                          ×
                        </button>
                      </Badge>
                    ))}
                  </div>
                </div>
                {formError && (
                  <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{formError}</div>
                )}
              </CardContent>
              <CardFooter className="flex justify-end gap-2 border-t pt-4">
                <Button type="button" variant="outline" onClick={() => { setIsEditModalOpen(false); resetForm(); }}>
                  Cancel
                </Button>
                <Button type="submit" disabled={updateMutation.isPending}>
                  {updateMutation.isPending ? "Updating..." : "Update Room"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}
    </div>
  );
}
