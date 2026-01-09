"use client";

import { useEffect, useState } from "react";
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
import { Badge } from "@/components/ui/badge";

const summaries = [
  "Freezing",
  "Bracing",
  "Chilly",
  "Cool",
  "Mild",
  "Warm",
  "Balmy",
  "Hot",
  "Sweltering",
  "Scorching",
];

const getTemperatureBadge = (tempC: number) => {
  if (tempC <= 0) return { variant: "default" as const, label: "Freezing" };
  if (tempC <= 10) return { variant: "secondary" as const, label: "Cold" };
  if (tempC <= 20) return { variant: "success" as const, label: "Mild" };
  if (tempC <= 30) return { variant: "warning" as const, label: "Warm" };
  return { variant: "destructive" as const, label: "Hot" };
};

export default function WeatherPage() {
  const [pageSize, setPageSize] = useState(10);
  const [currentPage, setCurrentPage] = useState(1);
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<string | undefined>();

  // Add modal state
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [editForecastId, setEditForecastId] = useState<string>("");
  const [editLocation, setEditLocation] = useState("");

  // Form state
  const [newLocation, setNewLocation] = useState("");
  const [newDate, setNewDate] = useState(new Date().toISOString().split("T")[0]);
  const [newTemperatureC, setNewTemperatureC] = useState(20);
  const [newSummary, setNewSummary] = useState("");
  const [formError, setFormError] = useState("");

  const { data: forecasts, isLoading, refetch } = trpc.weather.list.useQuery({
    pageNumber: currentPage,
    pageSize,
    waitForSortableUniqueId: lastSortableUniqueId,
  });

  const createMutation = trpc.weather.create.useMutation({
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

  const updateLocationMutation = trpc.weather.updateLocation.useMutation({
    onSuccess: (data) => {
      setLastSortableUniqueId(data.sortableUniqueId);
      refetch();
      setIsEditModalOpen(false);
    },
    onError: (error) => {
      setFormError(error.message);
    },
  });

  const removeMutation = trpc.weather.remove.useMutation({
    onSuccess: (data) => {
      setLastSortableUniqueId(data.sortableUniqueId);
      refetch();
    },
  });

  const resetForm = () => {
    setNewLocation("");
    setNewDate(new Date().toISOString().split("T")[0]);
    setNewTemperatureC(20);
    setNewSummary("");
    setFormError("");
  };

  useEffect(() => {
    if (!isAddModalOpen && !isEditModalOpen) return;

    const handleKey = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;

      if (isEditModalOpen) {
        setIsEditModalOpen(false);
        setFormError("");
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
    if (!newLocation || !newSummary) {
      setFormError("Please fill in all required fields");
      return;
    }
    createMutation.mutate({
      location: newLocation,
      date: newDate,
      temperatureC: newTemperatureC,
      summary: newSummary,
    });
  };

  const handleEditSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!editLocation) {
      setFormError("Location is required");
      return;
    }
    updateLocationMutation.mutate({
      forecastId: editForecastId,
      newLocationName: editLocation,
    });
  };

  const openEditModal = (forecastId: string, currentLocation: string) => {
    setEditForecastId(forecastId);
    setEditLocation(currentLocation);
    setFormError("");
    setIsEditModalOpen(true);
  };

  const celsiusToFahrenheit = (c: number) => 32 + Math.round(c / 0.5556);

  const totalForecasts = forecasts?.length ?? 0;
  const avgTemp = forecasts && forecasts.length > 0
    ? Math.round(forecasts.reduce((sum, f) => sum + f.temperatureC, 0) / forecasts.length)
    : 0;
  const uniqueLocations = forecasts ? new Set(forecasts.map(f => f.location)).size : 0;

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-foreground">Weather Forecasts</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Manage weather forecast data with CRUD operations
          </p>
        </div>
        <Button onClick={() => setIsAddModalOpen(true)} className="gap-2">
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          Add Forecast
        </Button>
      </div>

      {/* Stats Cards */}
      <div className="grid gap-4 md:grid-cols-3">
        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-primary/10">
                <svg className="h-6 w-6 text-primary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 15a4 4 0 004 4h9a5 5 0 10-.1-9.999 5.002 5.002 0 10-9.78 2.096A4.001 4.001 0 003 15z" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{totalForecasts}</p>
                <p className="text-sm text-muted-foreground">Total Forecasts</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-success/10">
                <svg className="h-6 w-6 text-success" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 11a3 3 0 11-6 0 3 3 0 016 0z" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{uniqueLocations}</p>
                <p className="text-sm text-muted-foreground">Locations</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-warning/10">
                <svg className="h-6 w-6 text-warning" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{avgTemp}°C</p>
                <p className="text-sm text-muted-foreground">Avg Temperature</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Table Section */}
      <Card>
        <CardHeader className="pb-4">
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="text-lg">Forecast List</CardTitle>
              <CardDescription>View and manage weather forecast entries</CardDescription>
            </div>
            <div className="flex items-center gap-3">
              <span className="text-sm text-muted-foreground">Show</span>
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
              <span className="text-sm text-muted-foreground">entries</span>
            </div>
          </div>
        </CardHeader>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="flex items-center justify-center py-12">
              <div className="flex items-center gap-3 text-muted-foreground">
                <svg className="h-5 w-5 animate-spin" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                </svg>
                Loading forecasts...
              </div>
            </div>
          ) : forecasts && forecasts.length > 0 ? (
            <div className="border-t">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Location</TableHead>
                    <TableHead>Date</TableHead>
                    <TableHead className="text-center">Temperature</TableHead>
                    <TableHead className="text-center">Condition</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {forecasts.map((forecast) => {
                    const tempBadge = getTemperatureBadge(forecast.temperatureC);
                    return (
                      <TableRow key={forecast.forecastId}>
                        <TableCell>
                          <div className="flex items-center gap-3">
                            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-primary/10 text-primary">
                              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
                              </svg>
                            </div>
                            <span className="font-medium">{forecast.location}</span>
                          </div>
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          {new Date(forecast.date).toLocaleDateString(undefined, {
                            weekday: 'short',
                            year: 'numeric',
                            month: 'short',
                            day: 'numeric'
                          })}
                        </TableCell>
                        <TableCell className="text-center">
                          <div className="flex flex-col items-center">
                            <span className="font-bold text-lg">{forecast.temperatureC}°C</span>
                            <span className="text-xs text-muted-foreground">{celsiusToFahrenheit(forecast.temperatureC)}°F</span>
                          </div>
                        </TableCell>
                        <TableCell className="text-center">
                          <Badge variant={tempBadge.variant}>{forecast.summary}</Badge>
                        </TableCell>
                        <TableCell className="text-right">
                          <div className="flex justify-end gap-2">
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => openEditModal(forecast.forecastId, forecast.location)}
                            >
                              Edit
                            </Button>
                            <Button
                              variant="destructive"
                              size="sm"
                              onClick={() => removeMutation.mutate({ forecastId: forecast.forecastId })}
                            >
                              Remove
                            </Button>
                          </div>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <div className="flex h-16 w-16 items-center justify-center rounded-full bg-muted mb-4">
                <svg className="h-8 w-8 text-muted-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 15a4 4 0 004 4h9a5 5 0 10-.1-9.999 5.002 5.002 0 10-9.78 2.096A4.001 4.001 0 003 15z" />
                </svg>
              </div>
              <h3 className="text-lg font-medium mb-1">No forecasts yet</h3>
              <p className="text-sm text-muted-foreground mb-4">Get started by adding your first weather forecast</p>
              <Button onClick={() => setIsAddModalOpen(true)}>Add Forecast</Button>
            </div>
          )}
        </CardContent>
        {forecasts && forecasts.length > 0 && (
          <CardFooter className="border-t py-4">
            <div className="flex w-full items-center justify-between">
              <p className="text-sm text-muted-foreground">Showing {forecasts.length} entries</p>
              <div className="flex items-center gap-2">
                <Button variant="outline" size="sm" onClick={() => setCurrentPage((p) => Math.max(1, p - 1))} disabled={currentPage === 1}>
                  Previous
                </Button>
                <span className="flex h-8 w-8 items-center justify-center rounded-md bg-primary text-sm font-medium text-primary-foreground">
                  {currentPage}
                </span>
                <Button variant="outline" size="sm" onClick={() => setCurrentPage((p) => p + 1)} disabled={!forecasts || forecasts.length < pageSize}>
                  Next
                </Button>
              </div>
            </div>
          </CardFooter>
        )}
      </Card>

      {/* Add Modal */}
      {isAddModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md animate-in fade-in zoom-in-95 duration-200">
            <form onSubmit={handleAddSubmit}>
              <CardHeader>
                <CardTitle>Add Weather Forecast</CardTitle>
                <CardDescription>Create a new weather forecast entry</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">Location</label>
                  <Input value={newLocation} onChange={(e) => setNewLocation(e.target.value)} placeholder="e.g., Tokyo, New York" autoFocus />
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Date</label>
                  <Input type="date" value={newDate} onChange={(e) => setNewDate(e.target.value)} />
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Temperature (°C)</label>
                  <Input type="number" value={newTemperatureC} onChange={(e) => setNewTemperatureC(Number(e.target.value))} min={-60} max={60} />
                  <p className="text-xs text-muted-foreground">Enter temperature in Celsius (-60 to 60)</p>
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Weather Condition</label>
                  <Select value={newSummary} onChange={(e) => setNewSummary(e.target.value)}>
                    <option value="">Select condition...</option>
                    {summaries.map((s) => (
                      <option key={s} value={s}>{s}</option>
                    ))}
                  </Select>
                </div>
                {formError && <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{formError}</div>}
              </CardContent>
              <CardFooter className="flex justify-end gap-2 border-t pt-4">
                <Button type="button" variant="outline" onClick={() => { setIsAddModalOpen(false); resetForm(); }}>Cancel</Button>
                <Button type="submit" disabled={createMutation.isPending}>
                  {createMutation.isPending ? "Adding..." : "Add Forecast"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* Edit Location Modal */}
      {isEditModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md animate-in fade-in zoom-in-95 duration-200">
            <form onSubmit={handleEditSubmit}>
              <CardHeader>
                <CardTitle>Edit Location</CardTitle>
                <CardDescription>Update the forecast location name</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">New Location</label>
                  <Input value={editLocation} onChange={(e) => setEditLocation(e.target.value)} placeholder="Enter new location" autoFocus />
                </div>
                {formError && <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">{formError}</div>}
              </CardContent>
              <CardFooter className="flex justify-end gap-2 border-t pt-4">
                <Button type="button" variant="outline" onClick={() => { setIsEditModalOpen(false); setFormError(""); }}>Cancel</Button>
                <Button type="submit" disabled={updateLocationMutation.isPending}>
                  {updateLocationMutation.isPending ? "Updating..." : "Update Location"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}
    </div>
  );
}
