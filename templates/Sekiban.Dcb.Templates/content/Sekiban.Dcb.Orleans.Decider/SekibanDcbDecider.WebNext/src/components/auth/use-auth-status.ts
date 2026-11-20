"use client";

import { trpc } from "@/lib/trpc";

/**
 * Auth status shared by the layout and RequireAuth. When the server reports that authentication is
 * temporarily unavailable (SERVICE_UNAVAILABLE, e.g. the API is restarting), the query keeps retrying
 * with backoff and `serviceUnavailable` is true, so the UI can say so instead of asking to log in.
 */
export function useAuthStatus() {
  const query = trpc.auth.status.useQuery(undefined, {
    staleTime: 0,
    refetchOnMount: "always",
    retry: (failureCount, error) => error.data?.code === "SERVICE_UNAVAILABLE" || failureCount < 3,
    retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 15000),
  });
  const serviceUnavailable = !query.data && (query.failureCount > 0 || query.isError);
  return { ...query, serviceUnavailable };
}
