"use client";

import { useMemo, useState } from "react";
import { trpc } from "@/lib/trpc";
import { RequireAuth } from "@/components/auth/require-auth";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
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

type ApprovalStatus = "Pending" | "Approved" | "Rejected";

const statusStyles: Record<ApprovalStatus, { variant: "default" | "success" | "warning" | "destructive"; label: string }> = {
  Pending: { variant: "warning", label: "Pending" },
  Approved: { variant: "success", label: "Approved" },
  Rejected: { variant: "destructive", label: "Rejected" },
};

const shortId = (id: string) => `${id.slice(0, 8)}...${id.slice(-4)}`;

const formatDateTime = (value: string) =>
  new Date(value).toLocaleString(undefined, { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });

export default function ApprovalsPage() {
  return (
    <RequireAuth>
      <ApprovalsContent />
    </RequireAuth>
  );
}

function ApprovalsContent() {
  const [pendingOnly, setPendingOnly] = useState(true);
  const [lastSortableUniqueId, setLastSortableUniqueId] = useState<string | undefined>();
  const [comments, setComments] = useState<Record<string, string>>({});

  const { data: authStatus } = trpc.auth.status.useQuery();
  const isAdmin = authStatus?.roles?.includes("Admin") ?? false;

  const {
    data: approvals,
    isLoading,
    error,
    refetch,
  } = trpc.approvals.list.useQuery(
    {
      pendingOnly,
      waitForSortableUniqueId: lastSortableUniqueId,
      pageNumber: 1,
      pageSize: 100,
    },
    { enabled: isAdmin }
  );

  const decideMutation = trpc.approvals.decide.useMutation({
    onSuccess: (data, variables) => {
      if (data.sortableUniqueId) {
        setLastSortableUniqueId(data.sortableUniqueId);
      }
      setComments((prev) => ({ ...prev, [variables.approvalRequestId]: "" }));
      refetch();
    },
  });

  const pendingCount = useMemo(
    () => approvals?.filter((item) => item.status === "Pending").length ?? 0,
    [approvals]
  );

  if (!isAdmin) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <Card className="w-full max-w-lg">
          <CardHeader>
            <CardTitle>Admin Access Required</CardTitle>
            <CardDescription>Only administrators can review approval requests.</CardDescription>
          </CardHeader>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-foreground">Approvals</h1>
          <p className="text-sm text-muted-foreground mt-1">Review and decide reservation approval requests</p>
        </div>
        <Select
          value={pendingOnly ? "pending" : "all"}
          onChange={(e) => setPendingOnly(e.target.value === "pending")}
          className="w-40"
        >
          <option value="pending">Pending only</option>
          <option value="all">All requests</option>
        </Select>
      </div>

      <div className="grid gap-4 md:grid-cols-3">
        <Card>
          <CardContent className="pt-6">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-warning/10 text-warning">
                <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>
              <div>
                <p className="text-2xl font-bold">{pendingCount}</p>
                <p className="text-sm text-muted-foreground">Pending approvals</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Approval Inbox</CardTitle>
          <CardDescription>Decide pending approvals and track their status</CardDescription>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex items-center justify-center py-12 text-muted-foreground">Loading approvals...</div>
          ) : error ? (
            <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">
              {error.message}
            </div>
          ) : approvals && approvals.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Request</TableHead>
                  <TableHead>Requester</TableHead>
                  <TableHead>Requested</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Comment</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {approvals.map((item) => {
                  const status = (item.status as ApprovalStatus) || "Pending";
                  const statusStyle = statusStyles[status] ?? statusStyles.Pending;
                  const commentValue = comments[item.approvalRequestId] ?? "";
                  const isPending = status === "Pending";
                  return (
                    <TableRow key={item.approvalRequestId}>
                      <TableCell>
                        <div className="font-medium">{shortId(item.approvalRequestId)}</div>
                        <div className="text-xs text-muted-foreground">
                          Reservation {shortId(item.reservationId)}
                        </div>
                        <div className="text-xs text-muted-foreground">
                          Room {shortId(item.roomId)}
                        </div>
                      </TableCell>
                      <TableCell>
                        <div className="text-sm">User {shortId(item.requesterId)}</div>
                        <div className="text-xs text-muted-foreground">
                          {item.approverIds.length > 0 ? `${item.approverIds.length} approvers` : "Any admin"}
                        </div>
                      </TableCell>
                      <TableCell>{formatDateTime(item.requestedAt)}</TableCell>
                      <TableCell>
                        <Badge variant={statusStyle.variant}>{statusStyle.label}</Badge>
                      </TableCell>
                      <TableCell>
                        <Input
                          value={commentValue}
                          onChange={(e) =>
                            setComments((prev) => ({ ...prev, [item.approvalRequestId]: e.target.value }))
                          }
                          placeholder="Optional comment"
                          disabled={!isPending}
                        />
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-2">
                          <Button
                            size="sm"
                            disabled={!isPending || decideMutation.isPending}
                            onClick={() =>
                              decideMutation.mutate({
                                approvalRequestId: item.approvalRequestId,
                                decision: "Approved",
                                comment: commentValue || undefined,
                              })
                            }
                          >
                            Approve
                          </Button>
                          <Button
                            size="sm"
                            variant="destructive"
                            disabled={!isPending || decideMutation.isPending}
                            onClick={() =>
                              decideMutation.mutate({
                                approvalRequestId: item.approvalRequestId,
                                decision: "Rejected",
                                comment: commentValue || undefined,
                              })
                            }
                          >
                            Reject
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          ) : (
            <div className="flex items-center justify-center py-12 text-muted-foreground">
              No approval requests found.
            </div>
          )}
        </CardContent>
        {decideMutation.error && (
          <CardFooter>
            <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive w-full">
              {decideMutation.error.message}
            </div>
          </CardFooter>
        )}
      </Card>
    </div>
  );
}
