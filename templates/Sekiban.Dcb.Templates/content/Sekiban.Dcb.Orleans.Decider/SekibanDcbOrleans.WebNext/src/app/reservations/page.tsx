"use client";

import { useState, useMemo, Fragment } from "react";
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
type ReservationDetails = {
  reservationId: string;
  roomId: string;
  organizerId: string;
  organizerName?: string;
  purpose: string;
  startTime: string;
  endTime: string;
  status: ReservationStatus;
  requiresApproval?: boolean;
  approvalRequestId?: string | null;
};
type ReservationLayout = {
  column: number;
  columns: number;
};

const statusConfig: Record<ReservationStatus, { variant: "default" | "secondary" | "success" | "warning" | "destructive"; label: string }> = {
  Draft: { variant: "secondary", label: "Draft" },
  Held: { variant: "warning", label: "Held" },
  Confirmed: { variant: "success", label: "Confirmed" },
  Cancelled: { variant: "destructive", label: "Cancelled" },
  Rejected: { variant: "destructive", label: "Rejected" },
  Expired: { variant: "default", label: "Expired" },
};
const SLOT_HEIGHT_PX = 60;
const MAX_VISIBLE_RESERVATIONS = 2;

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

const getSlotBounds = (dayStr: string, timeSlot: string) => {
  const slotStart = new Date(`${dayStr}T${timeSlot}:00`);
  const slotEnd = new Date(slotStart);
  slotEnd.setHours(slotEnd.getHours() + 1);
  return { slotStart, slotEnd };
};

const isReservationStartInSlot = (reservation: { startTime: string }, slotStart: Date, slotEnd: Date) => {
  const startDate = new Date(reservation.startTime);
  return startDate >= slotStart && startDate < slotEnd;
};

const getReservationBlockStyle = (
  reservation: { startTime: string; endTime: string },
  column = 0,
  columns = 1
) => {
  const startDate = new Date(reservation.startTime);
  const endDate = new Date(reservation.endTime);
  const durationMinutes = Math.max(0, (endDate.getTime() - startDate.getTime()) / 60000);
  const startOffset = (startDate.getMinutes() / 60) * SLOT_HEIGHT_PX;
  const height = Math.max(32, (durationMinutes / 60) * SLOT_HEIGHT_PX - 4);
  const edgePadding = 4;
  const gap = 6;
  let left = `${edgePadding}px`;
  let width = `calc(100% - ${edgePadding * 2}px)`;

  if (columns > 1) {
    const available = `calc(100% - ${edgePadding * 2 + (columns - 1) * gap}px)`;
    width = `calc(${available} / ${columns})`;
    left = `calc(${edgePadding}px + ${column} * (${available} / ${columns} + ${gap}px))`;
  }

  return {
    top: `${startOffset}px`,
    height: `${height}px`,
    left,
    width,
  };
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
  const [viewMode, setViewMode] = useState<"weekly" | "daily">("weekly");
  const [selectedDate, setSelectedDate] = useState(new Date());

  // Modal state
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isDetailsModalOpen, setIsDetailsModalOpen] = useState(false);
  const [isCancelModalOpen, setIsCancelModalOpen] = useState(false);
  const [selectedReservation, setSelectedReservation] = useState<ReservationDetails | null>(null);
  const [isOverflowModalOpen, setIsOverflowModalOpen] = useState(false);
  const [overflowReservations, setOverflowReservations] = useState<ReservationDetails[]>([]);
  const [overflowSlotLabel, setOverflowSlotLabel] = useState("");

  // Form state
  const [formData, setFormData] = useState({
    roomId: "",
    date: formatDate(new Date()),
    startTime: "09:00",
    endTime: "10:00",
    purpose: "",
    approvalRequestComment: "",
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

  const { data: authStatus } = trpc.auth.status.useQuery();
  const isAdmin = authStatus?.roles?.includes("Admin") ?? false;

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

  const confirmMutation = trpc.reservations.confirm.useMutation({
    onSuccess: (data) => {
      setLastSortableUniqueId(data.sortableUniqueId);
      refetch();
      setIsDetailsModalOpen(false);
      setSelectedReservation(null);
    },
    onError: (error) => {
      setFormError(error.message);
    },
  });

  const approveMutation = trpc.approvals.decide.useMutation({
    onSuccess: (data) => {
      if (data.sortableUniqueId) {
        setLastSortableUniqueId(data.sortableUniqueId);
      }
      refetch();
      setIsDetailsModalOpen(false);
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
      approvalRequestComment: "",
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
      approvalRequestComment: formData.approvalRequestComment || undefined,
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

  const openCreateModal = (date?: Date, time?: string, roomId?: string) => {
    setFormData({
      roomId: roomId ?? selectedRoomId ?? "",
      date: date ? formatDate(date) : formatDate(new Date()),
      startTime: time || "09:00",
      endTime: time ? `${(parseInt(time.split(":")[0]) + 1).toString().padStart(2, "0")}:00` : "10:00",
      purpose: "",
      approvalRequestComment: "",
    });
    setFormError("");
    setIsCreateModalOpen(true);
  };

  const openDetailsModal = (reservation: ReservationDetails) => {
    setSelectedReservation(reservation);
    setFormError("");
    setIsDetailsModalOpen(true);
    setIsOverflowModalOpen(false);
  };

  const openCancelModal = () => {
    setIsDetailsModalOpen(false);
    setIsCancelModalOpen(true);
    setCancelReason("");
    setFormError("");
  };

  const openOverflowModal = (label: string, reservations: ReservationDetails[]) => {
    setOverflowSlotLabel(label);
    setOverflowReservations(reservations);
    setIsOverflowModalOpen(true);
  };

  const handleAdminConfirm = () => {
    if (!selectedReservation) return;

    if (selectedReservation.requiresApproval && selectedReservation.approvalRequestId) {
      approveMutation.mutate({
        approvalRequestId: selectedReservation.approvalRequestId,
        decision: "Approved",
        comment: "Approved by admin",
      });
      return;
    }

    if (selectedReservation.requiresApproval && !selectedReservation.approvalRequestId) {
      setFormError("Approval request is missing for this reservation");
      return;
    }

    confirmMutation.mutate({
      reservationId: selectedReservation.reservationId,
      roomId: selectedReservation.roomId,
    });
  };

  const buildDayLayout = (items: ReservationDetails[]) => {
    const layout = new Map<string, ReservationLayout>();
    if (items.length === 0) return layout;

    const sorted = [...items].sort(
      (a, b) => new Date(a.startTime).getTime() - new Date(b.startTime).getTime()
    );

    let group: ReservationDetails[] = [];
    let groupEnd = 0;

    const applyGroupLayout = (groupItems: ReservationDetails[]) => {
      if (groupItems.length === 0) return;
      const active: { end: number; column: number }[] = [];
      const assignments = new Map<string, number>();
      let maxColumns = 0;

      for (const item of groupItems) {
        const start = new Date(item.startTime).getTime();
        const end = new Date(item.endTime).getTime();

        for (let i = active.length - 1; i >= 0; i--) {
          if (active[i].end <= start) {
            active.splice(i, 1);
          }
        }

        const used = new Set(active.map((entry) => entry.column));
        let column = 0;
        while (used.has(column)) {
          column += 1;
        }

        active.push({ end, column });
        assignments.set(item.reservationId, column);
        maxColumns = Math.max(maxColumns, active.length);
      }

      for (const item of groupItems) {
        const column = assignments.get(item.reservationId) ?? 0;
        layout.set(item.reservationId, { column, columns: maxColumns });
      }
    };

    for (const item of sorted) {
      const start = new Date(item.startTime).getTime();
      const end = new Date(item.endTime).getTime();

      if (group.length === 0) {
        group = [item];
        groupEnd = end;
        continue;
      }

      if (start < groupEnd) {
        group.push(item);
        groupEnd = Math.max(groupEnd, end);
      } else {
        applyGroupLayout(group);
        group = [item];
        groupEnd = end;
      }
    }

    applyGroupLayout(group);
    return layout;
  };

  const layoutByDay = useMemo(() => {
    if (!reservations) return new Map<string, Map<string, ReservationLayout>>();
    const byDay = new Map<string, ReservationDetails[]>();

    for (const res of reservations) {
      const details: ReservationDetails = {
        reservationId: res.reservationId,
        roomId: res.roomId,
        organizerId: res.organizerId,
        organizerName: res.organizerName,
        purpose: res.purpose,
        startTime: res.startTime,
        endTime: res.endTime,
        status: res.status as ReservationStatus,
        requiresApproval: res.requiresApproval,
        approvalRequestId: res.approvalRequestId,
      };
      const dayKey = formatDate(new Date(res.startTime));
      const existing = byDay.get(dayKey) ?? [];
      existing.push(details);
      byDay.set(dayKey, existing);
    }

    const layouts = new Map<string, Map<string, ReservationLayout>>();
    for (const [dayKey, items] of byDay) {
      layouts.set(dayKey, buildDayLayout(items));
    }
    return layouts;
  }, [reservations]);

  const layoutByDayRoom = useMemo(() => {
    if (!reservations) return new Map<string, Map<string, Map<string, ReservationLayout>>>();
    const byDayRoom = new Map<string, Map<string, ReservationDetails[]>>();

    for (const res of reservations) {
      const details: ReservationDetails = {
        reservationId: res.reservationId,
        roomId: res.roomId,
        organizerId: res.organizerId,
        organizerName: res.organizerName,
        purpose: res.purpose,
        startTime: res.startTime,
        endTime: res.endTime,
        status: res.status as ReservationStatus,
        requiresApproval: res.requiresApproval,
        approvalRequestId: res.approvalRequestId,
      };
      const dayKey = formatDate(new Date(res.startTime));
      const roomMap = byDayRoom.get(dayKey) ?? new Map<string, ReservationDetails[]>();
      const roomItems = roomMap.get(res.roomId) ?? [];
      roomItems.push(details);
      roomMap.set(res.roomId, roomItems);
      byDayRoom.set(dayKey, roomMap);
    }

    const layouts = new Map<string, Map<string, Map<string, ReservationLayout>>>();
    for (const [dayKey, roomMap] of byDayRoom) {
      const roomLayouts = new Map<string, Map<string, ReservationLayout>>();
      for (const [roomId, items] of roomMap) {
        roomLayouts.set(roomId, buildDayLayout(items));
      }
      layouts.set(dayKey, roomLayouts);
    }
    return layouts;
  }, [reservations]);

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
    const today = new Date();
    setCurrentWeek(today);
    setSelectedDate(today);
  };

  const previousDay = () => {
    const prev = new Date(selectedDate);
    prev.setDate(prev.getDate() - 1);
    setSelectedDate(prev);
  };

  const nextDay = () => {
    const next = new Date(selectedDate);
    next.setDate(next.getDate() + 1);
    setSelectedDate(next);
  };

  // Get reservations for a specific day and time slot
  const getReservationsForSlot = (day: Date, timeSlot: string) => {
    if (!reservations) return [];
    const dayStr = formatDate(day);
    const { slotStart, slotEnd } = getSlotBounds(dayStr, timeSlot);

    return reservations.filter((r) => {
      const startDate = new Date(r.startTime);
      const endDate = new Date(r.endTime);
      const resDateStr = formatDate(startDate);

      if (resDateStr !== dayStr) return false;
      return startDate < slotEnd && endDate > slotStart;
    });
  };

  const getReservationsForSlotByRoom = (day: Date, timeSlot: string, roomId: string) => {
    if (!reservations) return [];
    const dayStr = formatDate(day);
    const { slotStart, slotEnd } = getSlotBounds(dayStr, timeSlot);

    return reservations.filter((r) => {
      if (r.roomId !== roomId) return false;
      const startDate = new Date(r.startTime);
      const endDate = new Date(r.endTime);
      const resDateStr = formatDate(startDate);

      if (resDateStr !== dayStr) return false;
      return startDate < slotEnd && endDate > slotStart;
    });
  };

  // Get room name by ID
  const getRoomName = (roomId: string) => {
    const room = rooms?.find((r) => r.roomId === roomId);
    return room?.name || "Unknown Room";
  };

  const getOrganizerLabel = (reservation: { organizerId: string; organizerName?: string }) => {
    const name = reservation.organizerName?.trim();
    if (name) return name;
    return `User ${reservation.organizerId.slice(0, 8)}`;
  };

  const totalReservations = reservations?.length ?? 0;
  const confirmedReservations = reservations?.filter((r) => r.status === "Confirmed").length ?? 0;
  const pendingReservations = reservations?.filter((r) => r.status === "Held" || r.status === "Draft").length ?? 0;
  const selectedRoomRequiresApproval = Boolean(
    formData.roomId && rooms?.find((room) => room.roomId === formData.roomId)?.requiresApproval
  );
  const dailyRooms = useMemo(() => {
    if (!rooms) return [];
    if (selectedRoomId) {
      return rooms.filter((room) => room.roomId === selectedRoomId);
    }
    return rooms;
  }, [rooms, selectedRoomId]);
  const selectedDateLabel = selectedDate.toLocaleDateString(undefined, {
    month: "long",
    day: "numeric",
    year: "numeric",
  });

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
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <CardTitle className="text-lg">
                {viewMode === "weekly" ? "Weekly Schedule" : "Daily Schedule"}
              </CardTitle>
              <CardDescription>
                {viewMode === "weekly"
                  ? weekDays[0].toLocaleDateString(undefined, { month: "long", year: "numeric" })
                  : selectedDateLabel}
              </CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-4">
              <div className="flex items-center gap-2">
                <span className="text-sm text-muted-foreground">View:</span>
                <Select
                  value={viewMode}
                  onChange={(e) => setViewMode(e.target.value as "weekly" | "daily")}
                  className="w-36"
                >
                  <option value="weekly">Weekly</option>
                  <option value="daily">Daily</option>
                </Select>
              </div>
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
              {viewMode === "weekly" ? (
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
              ) : (
                <div className="flex items-center gap-2">
                  <Button variant="outline" size="sm" onClick={previousDay}>
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
                    </svg>
                  </Button>
                  <Input
                    type="date"
                    value={formatDate(selectedDate)}
                    onChange={(e) => {
                      if (!e.target.value) return;
                      setSelectedDate(new Date(`${e.target.value}T00:00:00`));
                    }}
                    className="w-[150px]"
                  />
                  <Button variant="outline" size="sm" onClick={goToToday}>
                    Today
                  </Button>
                  <Button variant="outline" size="sm" onClick={nextDay}>
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                    </svg>
                  </Button>
                </div>
              )}
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
          ) : viewMode === "weekly" ? (
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
                        const dayStr = formatDate(day);
                        const { slotStart, slotEnd } = getSlotBounds(dayStr, slot);
                        const dayKey = formatDate(day);
                        const layoutForDay = layoutByDay.get(dayKey) ?? new Map<string, ReservationLayout>();
                        const slotReservationDetails = slotReservations.map((res) => ({
                          reservationId: res.reservationId,
                          roomId: res.roomId,
                          organizerId: res.organizerId,
                          organizerName: res.organizerName,
                          purpose: res.purpose,
                          startTime: res.startTime,
                          endTime: res.endTime,
                          status: res.status as ReservationStatus,
                          requiresApproval: res.requiresApproval,
                          approvalRequestId: res.approvalRequestId,
                        }));
                        const startingReservations = slotReservationDetails.filter((res) =>
                          isReservationStartInSlot(res, slotStart, slotEnd)
                        );
                        const visibleReservations = startingReservations.filter((res) => {
                          const layout = layoutForDay.get(res.reservationId);
                          return (layout?.column ?? 0) < MAX_VISIBLE_RESERVATIONS;
                        });
                        const overflowCount = slotReservationDetails.filter((res) => {
                          const layout = layoutForDay.get(res.reservationId);
                          return (layout?.column ?? 0) >= MAX_VISIBLE_RESERVATIONS;
                        }).length;
                        const isToday = formatDate(day) === formatDate(new Date());
                        return (
                          <td
                            key={`${day.toISOString()}-${slot}`}
                            className={cn(
                              "p-1 align-top min-h-[60px] relative overflow-visible",
                              isToday ? "bg-primary/5" : "",
                              "hover:bg-muted/50 cursor-pointer transition-colors"
                            )}
                            onClick={() => {
                              if (slotReservations.length === 0) {
                                openCreateModal(day, slot);
                              }
                            }}
                          >
                            {visibleReservations.map((res) => {
                              const layout = layoutForDay.get(res.reservationId);
                              const column = layout?.column ?? 0;
                              const columns = Math.min(layout?.columns ?? 1, MAX_VISIBLE_RESERVATIONS);
                              return (
                              <div
                                key={res.reservationId}
                                onClick={(e) => {
                                  e.stopPropagation();
                                  openDetailsModal({
                                    reservationId: res.reservationId,
                                    roomId: res.roomId,
                                    organizerId: res.organizerId,
                                    organizerName: res.organizerName,
                                    purpose: res.purpose,
                                    startTime: res.startTime,
                                    endTime: res.endTime,
                                    status: res.status,
                                    requiresApproval: res.requiresApproval,
                                    approvalRequestId: res.approvalRequestId,
                                  });
                                }}
                                style={getReservationBlockStyle(res, column, columns)}
                                className={cn(
                                  "rounded px-2 py-1 text-xs cursor-pointer hover:opacity-80 transition-opacity absolute z-10",
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
                                <div className="text-[10px] opacity-75 truncate">
                                  {getOrganizerLabel(res)}
                                </div>
                              </div>
                            );
                            })}
                            {overflowCount > 0 && (
                              <button
                                type="button"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  openOverflowModal(
                                    `${day.toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric" })} ${slot}`,
                                    slotReservationDetails
                                  );
                                }}
                                className="absolute bottom-1 right-1 rounded-full bg-muted px-2 py-0.5 text-[10px] text-muted-foreground hover:bg-muted/80"
                              >
                                +{overflowCount}
                              </button>
                            )}
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div className="border-t">
              {dailyRooms.length === 0 ? (
                <div className="flex items-center justify-center py-12 text-muted-foreground">
                  No rooms available for this view.
                </div>
              ) : (
                <div className="min-w-[900px]">
                  <div
                    className="grid"
                    style={{
                      gridTemplateColumns: `80px repeat(${dailyRooms.length}, minmax(180px, 1fr))`,
                    }}
                  >
                    <div className="border-b bg-muted/50 p-3 text-left text-sm font-medium text-muted-foreground">
                      Time
                    </div>
                    {dailyRooms.map((room) => (
                      <div
                        key={room.roomId}
                        className="border-b bg-muted/50 p-3 text-center text-sm font-medium text-muted-foreground"
                      >
                        <div className="font-semibold text-foreground">{room.name}</div>
                        <div className="text-xs text-muted-foreground">Cap {room.capacity}</div>
                      </div>
                    ))}
                    {timeSlots.map((slot) => {
                      const dayKey = formatDate(selectedDate);
                      const { slotStart, slotEnd } = getSlotBounds(dayKey, slot);
                      return (
                        <Fragment key={slot}>
                          <div className="border-b p-3 text-sm text-muted-foreground font-medium">{slot}</div>
                          {dailyRooms.map((room) => {
                            const slotReservations = getReservationsForSlotByRoom(selectedDate, slot, room.roomId);
                            const layoutForRoom =
                              layoutByDayRoom.get(dayKey)?.get(room.roomId) ?? new Map<string, ReservationLayout>();
                            const slotReservationDetails = slotReservations.map((res) => ({
                              reservationId: res.reservationId,
                              roomId: res.roomId,
                              organizerId: res.organizerId,
                              organizerName: res.organizerName,
                              purpose: res.purpose,
                              startTime: res.startTime,
                              endTime: res.endTime,
                              status: res.status as ReservationStatus,
                              requiresApproval: res.requiresApproval,
                              approvalRequestId: res.approvalRequestId,
                            }));
                            const startingReservations = slotReservationDetails.filter((res) =>
                              isReservationStartInSlot(res, slotStart, slotEnd)
                            );
                            const visibleReservations = startingReservations.filter((res) => {
                              const layout = layoutForRoom.get(res.reservationId);
                              return (layout?.column ?? 0) < MAX_VISIBLE_RESERVATIONS;
                            });
                            const overflowCount = slotReservationDetails.filter((res) => {
                              const layout = layoutForRoom.get(res.reservationId);
                              return (layout?.column ?? 0) >= MAX_VISIBLE_RESERVATIONS;
                            }).length;

                            return (
                              <div
                                key={`${room.roomId}-${slot}`}
                                className="border-b border-l p-1 align-top min-h-[60px] relative overflow-visible hover:bg-muted/50 cursor-pointer transition-colors"
                                onClick={() => {
                                  if (slotReservations.length === 0) {
                                    openCreateModal(selectedDate, slot, room.roomId);
                                  }
                                }}
                              >
                                {visibleReservations.map((res) => {
                                  const layout = layoutForRoom.get(res.reservationId);
                                  const column = layout?.column ?? 0;
                                  const columns = Math.min(layout?.columns ?? 1, MAX_VISIBLE_RESERVATIONS);
                                  return (
                                    <div
                                      key={res.reservationId}
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        openDetailsModal(res);
                                      }}
                                      style={getReservationBlockStyle(res, column, columns)}
                                      className={cn(
                                        "rounded px-2 py-1 text-xs cursor-pointer hover:opacity-80 transition-opacity absolute z-10",
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
                                      <div className="text-[10px] opacity-75 truncate">{getOrganizerLabel(res)}</div>
                                    </div>
                                  );
                                })}
                                {overflowCount > 0 && (
                                  <button
                                    type="button"
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      openOverflowModal(
                                        `${selectedDate.toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric" })} ${slot}`,
                                        slotReservationDetails
                                      );
                                    }}
                                    className="absolute bottom-1 right-1 rounded-full bg-muted px-2 py-0.5 text-[10px] text-muted-foreground hover:bg-muted/80"
                                  >
                                    +{overflowCount}
                                  </button>
                                )}
                              </div>
                            );
                          })}
                        </Fragment>
                      );
                    })}
                  </div>
                </div>
              )}
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
                <div className="flex items-center gap-3 text-sm">
                  <svg className="w-4 h-4 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5.121 17.804A9 9 0 1118.879 6.196 9 9 0 015.12 17.804zM15 11a3 3 0 11-6 0 3 3 0 016 0z" />
                  </svg>
                  <span>{getOrganizerLabel(selectedReservation)}</span>
                </div>
              </div>
              {formError && (
                <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{formError}</div>
              )}
            </CardContent>
            <CardFooter className="flex justify-end gap-2 border-t pt-4">
              <Button variant="outline" onClick={() => setIsDetailsModalOpen(false)}>
                Close
              </Button>
              {isAdmin && selectedReservation.status === "Held" && (
                <Button onClick={handleAdminConfirm} disabled={confirmMutation.isPending || approveMutation.isPending}>
                  {selectedReservation.requiresApproval ? "Approve & Confirm" : "Confirm"}
                </Button>
              )}
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

      {/* Overflow Reservations Modal */}
      {isOverflowModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-lg animate-in fade-in zoom-in-95 duration-200">
            <CardHeader>
              <CardTitle>Reservations at {overflowSlotLabel}</CardTitle>
              <CardDescription>Multiple reservations start at the same time</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {overflowReservations.map((res) => (
                <button
                  key={res.reservationId}
                  type="button"
                  onClick={() => openDetailsModal(res)}
                  className="w-full text-left rounded-lg border border-border p-3 hover:bg-muted/50 transition-colors"
                >
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="font-medium">{res.purpose}</div>
                      <div className="text-xs text-muted-foreground">
                        {formatDateTime(res.startTime)} - {formatTime(res.endTime)}
                      </div>
                      <div className="text-xs text-muted-foreground">
                        {getRoomName(res.roomId)} · {getOrganizerLabel(res)}
                      </div>
                    </div>
                    <Badge variant={statusConfig[res.status]?.variant ?? "default"}>
                      {statusConfig[res.status]?.label ?? res.status}
                    </Badge>
                  </div>
                </button>
              ))}
            </CardContent>
            <CardFooter className="flex justify-end gap-2 border-t pt-4">
              <Button variant="outline" onClick={() => setIsOverflowModalOpen(false)}>
                Close
              </Button>
            </CardFooter>
          </Card>
        </div>
      )}
    </div>
  );
}
