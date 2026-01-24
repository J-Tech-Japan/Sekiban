import { z } from "zod";

// ProblemDetails schema for RFC 7807 error responses
const problemDetailsSchema = z.object({
  type: z.string().optional(),
  title: z.string().optional(),
  status: z.number().optional(),
  detail: z.string().optional(),
});

/**
 * Extracts a user-friendly error message from an API response.
 * Handles RFC 7807 ProblemDetails format and falls back to plain text.
 */
export async function extractErrorMessage(res: Response, fallback: string): Promise<string> {
  try {
    const text = await res.text();
    // Try to parse as ProblemDetails JSON
    const json = JSON.parse(text);
    const problemDetails = problemDetailsSchema.safeParse(json);
    if (problemDetails.success && problemDetails.data.detail) {
      return problemDetails.data.detail;
    }
    // Fall back to raw text if not ProblemDetails
    return text || fallback;
  } catch {
    return fallback;
  }
}
