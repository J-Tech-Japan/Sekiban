import { cookies } from "next/headers";
import { z } from "zod";

export const ACCESS_TOKEN_COOKIE = "bff_access_token";
export const REFRESH_TOKEN_COOKIE = "bff_refresh_token";
export const REFRESH_EXPIRES_COOKIE = "bff_refresh_expires";

type SessionTokens = {
  accessToken: string;
  refreshToken: string;
  refreshExpiresAt: Date;
};

/**
 * Get the access token for the current session.
 * Returns null if not authenticated.
 */
export async function getAccessToken(): Promise<string | null> {
  const cookieStore = await cookies();
  const accessToken = cookieStore.get(ACCESS_TOKEN_COOKIE)?.value;
  const refreshToken = cookieStore.get(REFRESH_TOKEN_COOKIE)?.value;
  const refreshExpires = cookieStore.get(REFRESH_EXPIRES_COOKIE)?.value;

  if (!accessToken || !refreshToken || !refreshExpires) {
    return null;
  }

  const refreshExpiresAt = new Date(refreshExpires);
  if (Number.isNaN(refreshExpiresAt.getTime()) || new Date() > refreshExpiresAt) {
    return null;
  }

  return accessToken;
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

export async function getSessionTokens(): Promise<SessionTokens | null> {
  const cookieStore = await cookies();
  const accessToken = cookieStore.get(ACCESS_TOKEN_COOKIE)?.value;
  const refreshToken = cookieStore.get(REFRESH_TOKEN_COOKIE)?.value;
  const refreshExpires = cookieStore.get(REFRESH_EXPIRES_COOKIE)?.value;

  if (!accessToken || !refreshToken || !refreshExpires) {
    return null;
  }

  const refreshExpiresAt = new Date(refreshExpires);
  if (Number.isNaN(refreshExpiresAt.getTime()) || new Date() > refreshExpiresAt) {
    return null;
  }

  return { accessToken, refreshToken, refreshExpiresAt };
}

export async function setSessionCookies(tokens: SessionTokens): Promise<void> {
  const cookieStore = await cookies();
  const cookieOptions = {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax" as const,
    maxAge: 60 * 60 * 24 * 7,
    path: "/",
  };

  cookieStore.set(ACCESS_TOKEN_COOKIE, tokens.accessToken, cookieOptions);
  cookieStore.set(REFRESH_TOKEN_COOKIE, tokens.refreshToken, cookieOptions);
  cookieStore.set(REFRESH_EXPIRES_COOKIE, tokens.refreshExpiresAt.toISOString(), cookieOptions);
}

export async function clearSessionCookies(): Promise<void> {
  const cookieStore = await cookies();
  cookieStore.delete(ACCESS_TOKEN_COOKIE);
  cookieStore.delete(REFRESH_TOKEN_COOKIE);
  cookieStore.delete(REFRESH_EXPIRES_COOKIE);
}

const refreshedTokensSchema = z.object({
  accessToken: z.string(),
  refreshToken: z.string(),
  refreshTokenExpires: z.string(),
});

// Refresh tokens are single use. Several server requests can find the same expired access token at
// once (auth.status, the SSE proxies, and requests sent before the browser received the new cookies),
// so every refresh of one refresh token shares a single exchange, and a successful result is reused for a
// short while. Without this, a second exchange of the already used token fails and logs the user out.
// This only coordinates requests inside one WebNext process; across several instances the API accepts a
// just-rotated refresh token for a short grace period (see the API's refresh endpoint).
const REFRESH_REUSE_MS = 60_000;
const REFRESH_TIMEOUT_MS = 10_000;

/**
 * Result of a refresh:
 * - "ok": new tokens were stored in the session cookies.
 * - "rejected": the API refused the refresh token (401/403). The session is over and callers treat
 *   the request as unauthenticated. Cookies are left as they are: another request may already have
 *   stored newer tokens, a later login overwrites them, and an explicit logout clears them.
 * - "unavailable": the refresh could not be completed (network error, timeout, 5xx such as 503 while
 *   the API starts). The session may still be valid, so callers must not clear the cookies.
 * - "no-session": there are no session cookies.
 */
export type RefreshOutcome =
  | { status: "ok"; accessToken: string }
  | { status: "rejected" }
  | { status: "unavailable" }
  | { status: "no-session" };

type ExchangeResult =
  | { status: "ok"; tokens: SessionTokens }
  | { status: "rejected" }
  | { status: "unavailable" };

const refreshStore = globalThis as typeof globalThis & {
  __sekibanSessionRefreshes?: Map<string, Promise<ExchangeResult>>;
};
const sessionRefreshes = (refreshStore.__sekibanSessionRefreshes ??= new Map());

async function exchangeRefreshToken(session: SessionTokens): Promise<ExchangeResult> {
  try {
    const refreshRes = await fetch(`${process.env.API_BASE_URL}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        accessToken: session.accessToken,
        refreshToken: session.refreshToken,
      }),
      cache: "no-store",
      signal: AbortSignal.timeout(REFRESH_TIMEOUT_MS),
    });
    if (refreshRes.status === 401 || refreshRes.status === 403) {
      return { status: "rejected" };
    }
    if (!refreshRes.ok) {
      return { status: "unavailable" };
    }

    const parsed = refreshedTokensSchema.safeParse(await refreshRes.json());
    if (!parsed.success) {
      return { status: "unavailable" };
    }

    return {
      status: "ok",
      tokens: {
        accessToken: parsed.data.accessToken,
        refreshToken: parsed.data.refreshToken,
        refreshExpiresAt: new Date(parsed.data.refreshTokenExpires),
      },
    };
  } catch {
    // Network error or timeout
    return { status: "unavailable" };
  }
}

/**
 * Exchange the stored refresh token for a new access token and store the new tokens in the
 * session cookies. Concurrent calls with the same refresh token share one exchange.
 */
export async function refreshSessionTokens(): Promise<RefreshOutcome> {
  const session = await getSessionTokens();
  if (!session) {
    return { status: "no-session" };
  }

  const key = session.refreshToken;
  let pending = sessionRefreshes.get(key);
  if (!pending) {
    const exchange = exchangeRefreshToken(session);
    pending = exchange;
    sessionRefreshes.set(key, exchange);
    // Keep only a successful result for reuse; a failed exchange is forgotten at once so the next
    // request tries again.
    void exchange.then((result) => {
      if (result.status !== "ok") {
        if (sessionRefreshes.get(key) === exchange) sessionRefreshes.delete(key);
        return;
      }
      setTimeout(() => {
        if (sessionRefreshes.get(key) === exchange) sessionRefreshes.delete(key);
      }, REFRESH_REUSE_MS).unref?.();
    });
  }

  const result = await pending;
  switch (result.status) {
    case "ok":
      await setSessionCookies(result.tokens);
      return { status: "ok", accessToken: result.tokens.accessToken };
    case "rejected":
      return { status: "rejected" };
    default:
      return { status: "unavailable" };
  }
}
