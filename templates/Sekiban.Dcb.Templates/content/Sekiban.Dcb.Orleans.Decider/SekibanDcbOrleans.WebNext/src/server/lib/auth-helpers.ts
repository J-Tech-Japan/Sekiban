import { cookies } from "next/headers";

// Token store - shared with auth router
// In production, use Redis or another distributed session store
export const tokenStore = new Map<string, { accessToken: string; refreshToken: string; expiresAt: Date }>();

export const SESSION_COOKIE_NAME = "bff_session";

/**
 * Get the access token for the current session.
 * Returns null if not authenticated.
 */
export async function getAccessToken(): Promise<string | null> {
  const cookieStore = await cookies();
  const sessionId = cookieStore.get(SESSION_COOKIE_NAME)?.value;

  if (!sessionId) {
    return null;
  }

  const session = tokenStore.get(sessionId);
  if (!session || new Date() > session.expiresAt) {
    return null;
  }

  return session.accessToken;
}

/**
 * Create headers with Authorization if authenticated.
 * Always includes Content-Type: application/json.
 */
export async function createAuthHeaders(): Promise<HeadersInit> {
  const accessToken = await getAccessToken();
  const headers: HeadersInit = {
    "Content-Type": "application/json",
  };

  if (accessToken) {
    headers["Authorization"] = `Bearer ${accessToken}`;
  }

  return headers;
}

/**
 * Throw an error if not authenticated.
 * Use this at the start of procedures that require authentication.
 */
export async function requireAuth(): Promise<string> {
  const accessToken = await getAccessToken();
  if (!accessToken) {
    throw new Error("Authentication required");
  }
  return accessToken;
}
