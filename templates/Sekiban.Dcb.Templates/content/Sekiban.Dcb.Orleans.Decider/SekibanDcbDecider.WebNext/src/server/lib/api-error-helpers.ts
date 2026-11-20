import { TRPCError } from "@trpc/server";
import { z } from "zod";

// ProblemDetails schema for RFC 7807 error responses. `errors` carries per-field validation
// messages; `error` is the shorter `{ error: "..." }` body some endpoints return.
const problemDetailsSchema = z.object({
  type: z.string().optional(),
  title: z.string().optional(),
  status: z.number().optional(),
  detail: z.string().optional(),
  error: z.string().optional(),
  errors: z.record(z.string(), z.union([z.array(z.string()), z.string()])).optional(),
});

/**
 * Extracts a user-friendly error message from an API response.
 * Handles RFC 7807 ProblemDetails format (including validation `errors`) and falls back to plain text.
 */
export async function extractErrorMessage(res: Response, fallback: string): Promise<string> {
  try {
    const text = await res.text();
    // Try to parse as ProblemDetails JSON
    const json = JSON.parse(text);
    const problemDetails = problemDetailsSchema.safeParse(json);
    if (problemDetails.success) {
      const { errors, detail, error, title } = problemDetails.data;
      const fieldMessages = Object.entries(errors ?? {}).flatMap(([field, messages]) =>
        (Array.isArray(messages) ? messages : [messages]).map((message) => `${field}: ${message}`)
      );
      if (fieldMessages.length > 0) {
        return fieldMessages.join("\n");
      }
      const message = detail || error || title;
      if (message) {
        return message;
      }
    }
    // Fall back to raw text if not ProblemDetails
    return text || fallback;
  } catch {
    return fallback;
  }
}

type TrpcErrorCode = ConstructorParameters<typeof TRPCError>[0]["code"];

const trpcCodeByStatus: Record<number, TrpcErrorCode> = {
  400: "BAD_REQUEST",
  401: "UNAUTHORIZED",
  403: "FORBIDDEN",
  404: "NOT_FOUND",
  409: "CONFLICT",
  429: "TOO_MANY_REQUESTS",
  503: "SERVICE_UNAVAILABLE",
};

/**
 * Build a tRPC error that keeps the API's HTTP status (for example 503 while the API is still
 * starting) instead of turning every upstream failure into a 500.
 */
export function apiStatusError(status: number, message: string): TRPCError {
  return new TRPCError({ code: trpcCodeByStatus[status] ?? "INTERNAL_SERVER_ERROR", message });
}
