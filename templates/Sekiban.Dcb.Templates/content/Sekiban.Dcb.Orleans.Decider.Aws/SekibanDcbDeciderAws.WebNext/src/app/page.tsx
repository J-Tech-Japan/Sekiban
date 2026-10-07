import Dashboard from "./Dashboard";

export default function DashboardPage() {
  return <Dashboard development={process.env.NODE_ENV === "development"} />;
}
