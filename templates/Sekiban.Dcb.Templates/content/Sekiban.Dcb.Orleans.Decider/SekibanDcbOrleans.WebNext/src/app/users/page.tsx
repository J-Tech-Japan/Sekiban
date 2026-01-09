"use client";

import { useEffect, useState } from "react";
import { trpc } from "@/lib/trpc";
import { RequireAuth } from "@/components/auth/require-auth";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
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

export default function UsersPage() {
  return (
    <RequireAuth>
      <UsersContent />
    </RequireAuth>
  );
}

function UsersContent() {
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<string | undefined>();
  const [limitEdits, setLimitEdits] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState("");

  const { data: authStatus } = trpc.auth.status.useQuery();
  const isAdmin = authStatus?.roles?.includes("Admin") ?? false;

  const {
    data: users,
    isLoading,
    refetch,
  } = trpc.users.list.useQuery(
    {
      pageNumber: 1,
      pageSize: 200,
      waitForSortableUniqueId: lastSortableUniqueId,
    },
    { enabled: isAdmin }
  );

  const updateMutation = trpc.users.updateMonthlyLimit.useMutation({
    onSuccess: (data) => {
      if (data.sortableUniqueId) {
        setLastSortableUniqueId(data.sortableUniqueId);
      } else {
        refetch();
      }
      setFormError("");
    },
    onError: (error) => {
      setFormError(error.message);
    },
  });

  useEffect(() => {
    if (!lastSortableUniqueId) return;
    refetch().finally(() => setLastSortableUniqueId(undefined));
  }, [lastSortableUniqueId, refetch]);

  if (!isAdmin) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <Card className="w-full max-w-lg">
          <CardHeader>
            <CardTitle>Admin Access Required</CardTitle>
            <CardDescription>Only administrators can manage user limits.</CardDescription>
          </CardHeader>
        </Card>
      </div>
    );
  }

  const totalUsers = users?.length ?? 0;
  const activeUsers = users?.filter((u) => u.isActive).length ?? 0;

  const hasLimitChange = (userId: string, currentLimit: number) =>
    (limitEdits[userId] ?? currentLimit.toString()) !== currentLimit.toString();

  const getLimitValue = (userId: string, currentLimit: number) =>
    limitEdits[userId] ?? currentLimit.toString();

  const handleLimitChange = (userId: string, value: string) => {
    setLimitEdits((prev) => ({ ...prev, [userId]: value }));
  };

  const handleSave = (userId: string, currentLimit: number) => {
    const rawValue = getLimitValue(userId, currentLimit);
    const parsed = Number.parseInt(rawValue, 10);
    if (Number.isNaN(parsed) || parsed <= 0) {
      setFormError("Monthly limit must be a positive number.");
      return;
    }
    updateMutation.mutate({ userId, monthlyReservationLimit: parsed });
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-foreground">Users</h1>
          <p className="text-sm text-muted-foreground mt-1">Manage monthly reservation limits</p>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-primary/10">
                <svg className="h-6 w-6 text-primary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4a4 4 0 110 8 4 4 0 010-8zM4 20a8 8 0 0116 0" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{totalUsers}</p>
                <p className="text-sm text-muted-foreground">Total Users</p>
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
                <p className="text-2xl font-bold">{activeUsers}</p>
                <p className="text-sm text-muted-foreground">Active Users</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>User Directory</CardTitle>
          <CardDescription>Update monthly reservation limits per user</CardDescription>
        </CardHeader>
        <CardContent>
          {formError && (
            <div className="mb-4 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-2 text-sm text-destructive">
              {formError}
            </div>
          )}
          {isLoading ? (
            <div className="py-8 text-center text-muted-foreground">Loading users...</div>
          ) : users && users.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>User</TableHead>
                  <TableHead>Email</TableHead>
                  <TableHead>Department</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Roles</TableHead>
                  <TableHead className="w-[160px]">Monthly Limit</TableHead>
                  <TableHead>Providers</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {users.map((user) => (
                  <TableRow key={user.userId}>
                    <TableCell className="font-medium">{user.displayName}</TableCell>
                    <TableCell>{user.email}</TableCell>
                    <TableCell>{user.department ?? "—"}</TableCell>
                    <TableCell>
                      <Badge variant={user.isActive ? "success" : "secondary"}>
                        {user.isActive ? "Active" : "Deactivated"}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {user.roles.length > 0 ? (
                        <div className="flex flex-wrap gap-1">
                          {user.roles.map((role) => (
                            <Badge
                              key={role}
                              variant={role === "Admin" ? "destructive" : "outline"}
                            >
                              {role}
                            </Badge>
                          ))}
                        </div>
                      ) : (
                        "—"
                      )}
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        min={1}
                        value={getLimitValue(user.userId, user.monthlyReservationLimit)}
                        onChange={(event) => handleLimitChange(user.userId, event.target.value)}
                        className="h-9"
                      />
                    </TableCell>
                    <TableCell>
                      {user.externalProviders.length > 0 ? (
                        <div className="flex flex-wrap gap-1">
                          {user.externalProviders.map((provider) => (
                            <Badge key={provider} variant="outline">
                              {provider}
                            </Badge>
                          ))}
                        </div>
                      ) : (
                        "—"
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => handleSave(user.userId, user.monthlyReservationLimit)}
                        disabled={!hasLimitChange(user.userId, user.monthlyReservationLimit) || updateMutation.isPending}
                      >
                        Save
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <div className="py-8 text-center text-muted-foreground">No users found.</div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
