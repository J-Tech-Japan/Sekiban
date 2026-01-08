"use client";

import { useState, useMemo } from "react";
import { trpc } from "@/lib/trpc";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { RequireAuth } from "@/components/auth/require-auth";

type ReservationStatus = "Draft" | "Held" | "Confirmed" | "Cancelled" | "Rejected" | "Expired";

const statusConfig: Record<ReservationStatus, { variant: "default" | "secondary" | "success" | "warning" | "destructive"; label: string }> = {
  Draft: { variant: "secondary", label: "Draft" },
  Held: { variant: "warning", label: "Held" },
  Confirmed: { variant: "success", label: "Confirmed" },
  Cancelled: { variant: "destructive", label: "Cancelled" },
  Rejected: { variant: "destructive", label: "Rejected" },
  Expired: { variant: "default", label: "Expired" },
};

// Helper to generate week days
const getWeekDays = (date: Date) => {
  const start = new Date(date);
  start.setDate(start.getDate() - start.getDay() + 1); // Start from Monday
  const days: Date[] = [];
  for (let i = 0; i < 7; i++) {
    const day = new Date(start);
    day.setDate(start.getDate() + i);
    days.push(day);
  }
  return days;
};

// Helper to generate time slots
const getTimeSlots = () => {
  const slots: string[] = [];
  for (let hour = 8; hour <= 20; hour++) {
    slots.push(`${hour.toString().padStart(2, "0")}:00`);
  }
  return slots;
};

const formatDate = (date: Date) => {
  return date.toISOString().split("T")[0];
};

const formatTime = (dateStr: string) => {
  const date = new Date(dateStr);
  return date.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit", hour12: false });
};

const formatDateTime = (dateStr: string) => {
  const date = new Date(dateStr);
  return date.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
};

export default function ReservationsPage() {
  return (
    <RequireAuth>
      <ReservationsContent />
    </RequireAuth>
  );
}

function ReservationsContent() {
  const [currentWeek, setCurrentWeek] = useState(new Date());
  const [selectedRoomId, setSelectedRoomId] = useState<string>("");
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<string | undefined>();

  // Modal state
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isDetailsModalOpen, setIsDetailsModalOpen] = useState(false);
  const [isCancelModalOpen, setIsCancelModalOpen] = useState(false);
  const [selectedReservation, setSelectedReservation] = useState<{
    reservationId: string;
    roomId: string;
    purpose: string;
    startTime: string;
    endTime: string;
    status: ReservationStatus;
  } | null>(null);

  // Form state
  const [formData, setFormData] = useState({
    roomId: "",
    date: formatDate(new Date()),
    startTime: "09:00",
    endTime: "10:00",
    purpose: "",
  });
  const [cancelReason, setCancelReason] = useState("");
  const [formError, setFormError] = useState("");

  const weekDays = useMemo(() => getWeekDays(currentWeek), [currentWeek]);
  const timeSlots = useMemo(() => getTimeSlots(), []);

  const { data: rooms } = trpc.rooms.list.useQuery({});

  const { data: reservations, isLoading, refetch } = trpc.reservations.list.useQuery({
    roomId: selectedRoomId || undefined,
    waitForSortableUniqueId: lastSortableUniqueId,
  });

  const quickReserveMutation = trpc.reservations.quickReserve.useMutation({
    onSuccess: (data) => {
      setLastSortableUniqueId(data.sortableUniqueId);
      refetch();
      setIsCreateModalOpen(false);
      resetForm();
    },
    onError: (error) => {
      setFormError(error.message);
    },
  });

  const cancelMutation = trpc.reservations.cancel.useMutation({
    onSuccess: (data) => {
      setLastSortableUniqueId(data.sortableUniqueId);
      refetch();
      setIsCancelModalOpen(false);
      setCancelReason("");
      setSelectedReservation(null);
    },
    onError: (error) => {
      setFormError(error.message);
    },
  });

  const resetForm = () => {
    setFormData({
      roomId: selectedRoomId || "",
      date: formatDate(new Date()),
      startTime: "09:00",
      endTime: "10:00",
      purpose: "",
    });
    setFormError("");
  };

  const handleCreateSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.roomId || !formData.purpose) {
      setFormError("Please fill in all required fields");
      return;
    }

    const startDateTime = new Date(`${formData.date}T${formData.startTime}:00`);
    const endDateTime = new Date(`${formData.date}T${formData.endTime}:00`);

    if (endDateTime <= startDateTime) {
      setFormError("End time must be after start time");
      return;
    }

    quickReserveMutation.mutate({
      roomId: formData.roomId,
      startTime: startDateTime.toISOString(),
      endTime: endDateTime.toISOString(),
      purpose: formData.purpose,
    });
  };

  const handleCancelSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedReservation || !cancelReason) {
      setFormError("Please provide a cancellation reason");
      return;
    }
    cancelMutation.mutate({
      reservationId: selectedReservation.reservationId,
      roomId: selectedReservation.roomId,
      reason: cancelReason,
    });
  };

  const openCreateModal = (date?: Date, time?: string) => {
    setFormData({
      roomId: selectedRoomId || "",
      date: date ? formatDate(date) : formatDate(new Date()),
      startTime: time || "09:00",
      endTime: time ? `${(parseInt(time.split(":")[0]) + 1).toString().padStart(2, "0")}:00` : "10:00",
      purpose: "",
    });
    setFormError("");
    setIsCreateModalOpen(true);
  };

  const openDetailsModal = (reservation: typeof selectedReservation) => {
    setSelectedReservation(reservation);
    setIsDetailsModalOpen(true);
  };

  const openCancelModal = () => {
    setIsDetailsModalOpen(false);
    setIsCancelModalOpen(true);
    setCancelReason("");
    setFormError("");
  };

  const previousWeek = () => {
    const prev = new Date(currentWeek);
    prev.setDate(prev.getDate() - 7);
    setCurrentWeek(prev);
  };

  const nextWeek = () => {
    const next = new Date(currentWeek);
    next.setDate(next.getDate() + 7);
    setCurrentWeek(next);
  };

  const goToToday = () => {
    setCurrentWeek(new Date());
  };

  // Get reservations for a specific day and time slot
  const getReservationsForSlot = (day: Date, timeSlot: string) => {
    if (!reservations) return [];
    const dayStr = formatDate(day);
    const slotHour = parseInt(timeSlot.split(":")[0]);

    return reservations.filter((r) => {
      const startDate = new Date(r.startTime);
      const endDate = new Date(r.endTime);
      const resDateStr = formatDate(startDate);

      if (resDateStr !== dayStr) return false;

      const startHour = startDate.getHours();
      const endHour = endDate.getHours();

      return slotHour >= startHour && slotHour < endHour;
    });
  };

  // Get room name by ID
  const getRoomName = (roomId: string) => {
    const room = rooms?.find((r) => r.roomId === roomId);
    return room?.name || "Unknown Room";
  };

  const totalReservations = reservations?.length ?? 0;
  const confirmedReservations = reservations?.filter((r) => r.status === "Confirmed").length ?? 0;
  const pendingReservations = reservations?.filter((r) => r.status === "Held" || r.status === "Draft").length ?? 0;

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-foreground">Reservations</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Book meeting rooms and manage reservations
          </p>
        </div>
        <Button onClick={() => openCreateModal()} className="gap-2">
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          New Reservation
        </Button>
      </div>

      {/* Stats Cards */}
      <div className="grid gap-4 md:grid-cols-3">
        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-primary/10">
                <svg className="h-6 w-6 text-primary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{totalReservations}</p>
                <p className="text-sm text-muted-foreground">Total Reservations</p>
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
                <p className="text-2xl font-bold">{confirmedReservations}</p>
                <p className="text-sm text-muted-foreground">Confirmed</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-warning/10">
                <svg className="h-6 w-6 text-warning" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{pendingReservations}</p>
                <p className="text-sm text-muted-foreground">Pending</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Calendar Section */}
      <Card>
        <CardHeader className="pb-4">
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="text-lg">Weekly Schedule</CardTitle>
              <CardDescription>
                {weekDays[0].toLocaleDateString(undefined, { month: "long", year: "numeric" })}
              </CardDescription>
            </div>
            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2">
                <span className="text-sm text-muted-foreground">Room:</span>
                <Select
                  value={selectedRoomId}
                  onChange={(e) => setSelectedRoomId(e.target.value)}
                  className="w-48"
                >
                  <option value="">All Rooms</option>
                  {rooms?.map((room) => (
                    <option key={room.roomId} value={room.roomId}>
                      {room.name}
                    </option>
                  ))}
                </Select>
              </div>
              <div className="flex items-center gap-1">
                <Button variant="outline" size="sm" onClick={previousWeek}>
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
                  </svg>
                </Button>
                <Button variant="outline" size="sm" onClick={goToToday}>
                  Today
                </Button>
                <Button variant="outline" size="sm" onClick={nextWeek}>
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                  </svg>
                </Button>
              </div>
            </div>
          </div>
        </CardHeader>
        <CardContent className="p-0 overflow-x-auto">
          {isLoading ? (
            <div className="flex items-center justify-center py-12">
              <div className="flex items-center gap-3 text-muted-foreground">
                <svg className="h-5 w-5 animate-spin" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                </svg>
                Loading schedule...
              </div>
            </div>
          ) : (
            <div className="border-t">
              <table className="w-full min-w-[800px]">
                <thead>
                  <tr className="border-b bg-muted/50">
                    <th className="w-20 p-3 text-left text-sm font-medium text-muted-foreground">Time</th>
                    {weekDays.map((day) => (
                      <th
                        key={day.toISOString()}
                        className={cn(
                          "p-3 text-center text-sm font-medium",
                          formatDate(day) === formatDate(new Date())
                            ? "bg-primary/10 text-primary"
                            : "text-muted-foreground"
                        )}
                      >
                        <div>{day.toLocaleDateString(undefined, { weekday: "short" })}</div>
                        <div className="text-lg font-semibold">{day.getDate()}</div>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {timeSlots.map((slot) => (
                    <tr key={slot} className="border-b">
                      <td className="p-3 text-sm text-muted-foreground font-medium">{slot}</td>
                      {weekDays.map((day) => {
                        const slotReservations = getReservationsForSlot(day, slot);
                        const isToday = formatDate(day) === formatDate(new Date());
                        return (
                          <td
                            key={`${day.toISOString()}-${slot}`}
                            className={cn(
                              "p-1 align-top min-h-[60px] relative",
                              isToday ? "bg-primary/5" : "",
                              "hover:bg-muted/50 cursor-pointer transition-colors"
                            )}
                            onClick={() => {
                              if (slotReservations.length === 0) {
                                openCreateModal(day, slot);
                              }
                            }}
                          >
                            {slotReservations.map((res) => (
                              <div
                                key={res.reservationId}
                                onClick={(e) => {
                                  e.stopPropagation();
                                  openDetailsModal({
                                    reservationId: res.reservationId,
                                    roomId: res.roomId,
                                    purpose: res.purpose,
                                    startTime: res.startTime,
                                    endTime: res.endTime,
                                    status: res.status as ReservationStatus,
                                  });
                                }}
                                className={cn(
                                  "rounded px-2 py-1 text-xs mb-1 cursor-pointer hover:opacity-80 transition-opacity",
                                  res.status === "Confirmed"
                                    ? "bg-success/20 text-success border border-success/30"
                                    : res.status === "Held"
                                    ? "bg-warning/20 text-warning border border-warning/30"
                                    : res.status === "Draft"
                                    ? "bg-muted text-muted-foreground border border-muted-foreground/30"
                                    : "bg-destructive/20 text-destructive border border-destructive/30"
                                )}
                              >
                                <div className="font-medium truncate">{res.purpose}</div>
                                <div className="text-[10px] opacity-75">
                                  {!selectedRoomId && getRoomName(res.roomId)}
                                </div>
                              </div>
                            ))}
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
        <CardFooter className="border-t py-4">
          <div className="flex items-center gap-4 text-sm text-muted-foreground">
            <div className="flex items-center gap-2">
              <div className="w-3 h-3 rounded bg-success/20 border border-success/30" />
              <span>Confirmed</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-3 h-3 rounded bg-warning/20 border border-warning/30" />
              <span>Held (Pending)</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-3 h-3 rounded bg-muted border border-muted-foreground/30" />
              <span>Draft</span>
            </div>
          </div>
        </CardFooter>
      </Card>

      {/* Create Reservation Modal */}
      {isCreateModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md animate-in fade-in zoom-in-95 duration-200">
            <form onSubmit={handleCreateSubmit}>
              <CardHeader>
                <CardTitle>New Reservation</CardTitle>
                <CardDescription>Book a meeting room</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">Room *</label>
                  <Select
                    value={formData.roomId}
                    onChange={(e) => setFormData({ ...formData, roomId: e.target.value })}
                  >
                    <option value="">Select a room...</option>
                    {rooms?.filter((r) => r.isActive).map((room) => (
                      <option key={room.roomId} value={room.roomId}>
                        {room.name} (Capacity: {room.capacity})
                      </option>
                    ))}
                  </Select>
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Date</label>
                  <Input
                    type="date"
                    value={formData.date}
                    onChange={(e) => setFormData({ ...formData, date: e.target.value })}
                  />
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <label className="text-sm font-medium">Start Time</label>
                    <Select
                      value={formData.startTime}
                      onChange={(e) => setFormData({ ...formData, startTime: e.target.value })}
                    >
                      {timeSlots.map((slot) => (
                        <option key={slot} value={slot}>
                          {slot}
                        </option>
                      ))}
                    </Select>
                  </div>
                  <div className="space-y-2">
                    <label className="text-sm font-medium">End Time</label>
                    <Select
                      value={formData.endTime}
                      onChange={(e) => setFormData({ ...formData, endTime: e.target.value })}
                    >
                      {timeSlots.map((slot) => (
                        <option key={slot} value={slot}>
                          {slot}
                        </option>
                      ))}
                    </Select>
                  </div>
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Purpose *</label>
                  <Input
                    value={formData.purpose}
                    onChange={(e) => setFormData({ ...formData, purpose: e.target.value })}
                    placeholder="e.g., Team Meeting, Client Presentation"
                    autoFocus
                  />
                </div>
                {formError && (
                  <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{formError}</div>
                )}
              </CardContent>
              <CardFooter className="flex justify-end gap-2 border-t pt-4">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => {
                    setIsCreateModalOpen(false);
                    resetForm();
                  }}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={quickReserveMutation.isPending}>
                  {quickReserveMutation.isPending ? "Booking..." : "Book Room"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* Reservation Details Modal */}
      {isDetailsModalOpen && selectedReservation && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md animate-in fade-in zoom-in-95 duration-200">
            <CardHeader>
              <div className="flex items-center justify-between">
                <CardTitle>Reservation Details</CardTitle>
                <Badge variant={statusConfig[selectedReservation.status].variant}>
                  {statusConfig[selectedReservation.status].label}
                </Badge>
              </div>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-3">
                <div className="flex items-center gap-3 text-sm">
                  <svg className="w-4 h-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5" />
                  </svg>
                  <span className="font-medium">{getRoomName(selectedReservation.roomId)}</span>
                </div>
                <div className="flex items-center gap-3 text-sm">
                  <svg className="w-4 h-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                  </svg>
                  <span>{formatDateTime(selectedReservation.startTime)} - {formatTime(selectedReservation.endTime)}</span>
                </div>
                <div className="flex items-start gap-3 text-sm">
                  <svg className="w-4 h-4 text-muted-foreground mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                  </svg>
                  <span>{selectedReservation.purpose}</span>
                </div>
              </div>
            </CardContent>
            <CardFooter className="flex justify-end gap-2 border-t pt-4">
              <Button variant="outline" onClick={() => setIsDetailsModalOpen(false)}>
                Close
              </Button>
              {(selectedReservation.status === "Confirmed" || selectedReservation.status === "Held" || selectedReservation.status === "Draft") && (
                <Button variant="destructive" onClick={openCancelModal}>
                  Cancel Reservation
                </Button>
              )}
            </CardFooter>
          </Card>
        </div>
      )}

      {/* Cancel Confirmation Modal */}
      {isCancelModalOpen && selectedReservation && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md animate-in fade-in zoom-in-95 duration-200">
            <form onSubmit={handleCancelSubmit}>
              <CardHeader>
                <CardTitle>Cancel Reservation</CardTitle>
                <CardDescription>
                  Are you sure you want to cancel this reservation? This action cannot be undone.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="rounded-md bg-muted p-3 text-sm">
                  <div className="font-medium">{selectedReservation.purpose}</div>
                  <div className="text-muted-foreground">
                    {formatDateTime(selectedReservation.startTime)} - {formatTime(selectedReservation.endTime)}
                  </div>
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Cancellation Reason *</label>
                  <Input
                    value={cancelReason}
                    onChange={(e) => setCancelReason(e.target.value)}
                    placeholder="e.g., Meeting rescheduled"
                    autoFocus
                  />
                </div>
                {formError && (
                  <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{formError}</div>
                )}
              </CardContent>
              <CardFooter className="flex justify-end gap-2 border-t pt-4">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => {
                    setIsCancelModalOpen(false);
                    setCancelReason("");
                    setSelectedReservation(null);
                  }}
                >
                  Keep Reservation
                </Button>
                <Button type="submit" variant="destructive" disabled={cancelMutation.isPending}>
                  {cancelMutation.isPending ? "Cancelling..." : "Cancel Reservation"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}
    </div>
  );
}
