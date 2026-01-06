export default function HomePage() {
  return (
    <div>
      <h1 className="text-3xl font-bold mb-4">Welcome to Sekiban DCB Orleans</h1>
      <p className="text-muted-foreground mb-6">
        This is a sample application demonstrating the Sekiban DCB (Dynamic
        Consistency Boundary) pattern with Microsoft Orleans.
      </p>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <div className="rounded-lg border bg-card p-6">
          <h3 className="font-semibold mb-2">Weather Forecasts</h3>
          <p className="text-sm text-muted-foreground">
            Manage weather forecast data with CRUD operations.
          </p>
        </div>
        <div className="rounded-lg border bg-card p-6">
          <h3 className="font-semibold mb-2">Students</h3>
          <p className="text-sm text-muted-foreground">
            Register and manage student records.
          </p>
        </div>
        <div className="rounded-lg border bg-card p-6">
          <h3 className="font-semibold mb-2">Classrooms</h3>
          <p className="text-sm text-muted-foreground">
            Create and manage classroom capacity.
          </p>
        </div>
        <div className="rounded-lg border bg-card p-6">
          <h3 className="font-semibold mb-2">Enrollments</h3>
          <p className="text-sm text-muted-foreground">
            Enroll students in classrooms and manage registrations.
          </p>
        </div>
      </div>
    </div>
  );
}
