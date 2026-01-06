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

export default function WeatherPage() {
  const [pageSize, setPageSize] = useState(10);
  const [currentPage, setCurrentPage] = useState(1);
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<
    string | undefined
  >();

  // Add modal state
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [editForecastId, setEditForecastId] = useState<string>("");
  const [editLocation, setEditLocation] = useState("");

  // Form state
  const [newLocation, setNewLocation] = useState("");
  const [newDate, setNewDate] = useState(
    new Date().toISOString().split("T")[0]
  );
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

  if (isLoading) {
    return <p>Loading...</p>;
  }

  return (
    <div>
      <h1 className="text-3xl font-bold mb-4">Weather</h1>

      <Button onClick={() => setIsAddModalOpen(true)} className="mb-4">
        Add New Weather Forecast
      </Button>

      <h3 className="text-xl font-semibold mb-3">Weather Forecasts</h3>

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
          Showing {forecasts?.length ?? 0} items
        </span>
      </div>

      {forecasts && forecasts.length > 0 ? (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Location</TableHead>
              <TableHead>Date</TableHead>
              <TableHead>Temp. (C)</TableHead>
              <TableHead>Temp. (F)</TableHead>
              <TableHead>Summary</TableHead>
              <TableHead>Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {forecasts.map((forecast) => (
              <TableRow key={forecast.forecastId}>
                <TableCell>{forecast.location}</TableCell>
                <TableCell>
                  {new Date(forecast.date).toLocaleDateString()}
                </TableCell>
                <TableCell>{forecast.temperatureC}</TableCell>
                <TableCell>
                  {celsiusToFahrenheit(forecast.temperatureC)}
                </TableCell>
                <TableCell>{forecast.summary}</TableCell>
                <TableCell>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() =>
                        openEditModal(forecast.forecastId, forecast.location)
                      }
                    >
                      Edit Location
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      onClick={() =>
                        removeMutation.mutate({
                          forecastId: forecast.forecastId,
                        })
                      }
                    >
                      Remove
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      ) : (
        <p className="text-muted-foreground">
          No weather forecasts available. Click &quot;Add New Weather
          Forecast&quot; to create one.
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
            disabled={!forecasts || forecasts.length < pageSize}
          >
            Next
          </Button>
        </div>
      </div>

      {/* Add Modal */}
      {isAddModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <Card className="w-full max-w-md">
            <form onSubmit={handleAddSubmit}>
              <CardHeader>
                <CardTitle>Add Weather Forecast</CardTitle>
                <CardDescription>
                  Create a new weather forecast entry
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div>
                  <label className="text-sm font-medium">Location</label>
                  <Input
                    value={newLocation}
                    onChange={(e) => setNewLocation(e.target.value)}
                    placeholder="Enter location"
                  />
                </div>
                <div>
                  <label className="text-sm font-medium">Date</label>
                  <Input
                    type="date"
                    value={newDate}
                    onChange={(e) => setNewDate(e.target.value)}
                  />
                </div>
                <div>
                  <label className="text-sm font-medium">Temperature (C)</label>
                  <Input
                    type="number"
                    value={newTemperatureC}
                    onChange={(e) => setNewTemperatureC(Number(e.target.value))}
                    min={-60}
                    max={60}
                  />
                </div>
                <div>
                  <label className="text-sm font-medium">Summary</label>
                  <Select
                    value={newSummary}
                    onChange={(e) => setNewSummary(e.target.value)}
                  >
                    <option value="">Select a summary...</option>
                    {summaries.map((s) => (
                      <option key={s} value={s}>
                        {s}
                      </option>
                    ))}
                  </Select>
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
                  {createMutation.isPending ? "Adding..." : "Add Forecast"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* Edit Location Modal */}
      {isEditModalOpen && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <Card className="w-full max-w-md">
            <form onSubmit={handleEditSubmit}>
              <CardHeader>
                <CardTitle>Edit Location</CardTitle>
                <CardDescription>Update the forecast location</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div>
                  <label className="text-sm font-medium">New Location</label>
                  <Input
                    value={editLocation}
                    onChange={(e) => setEditLocation(e.target.value)}
                    placeholder="Enter new location"
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
                    setIsEditModalOpen(false);
                    setFormError("");
                  }}
                >
                  Cancel
                </Button>
                <Button
                  type="submit"
                  disabled={updateLocationMutation.isPending}
                >
                  {updateLocationMutation.isPending
                    ? "Updating..."
                    : "Update Location"}
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}
    </div>
  );
}
