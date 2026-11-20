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
const refreshStore = globalThis as typeof globalThis & {
  __sekibanSessionRefreshes?: Map<string, Promise<SessionTokens | null>>;
};
const sessionRefreshes = (refreshStore.__sekibanSessionRefreshes ??= new Map());

async function exchangeRefreshToken(session: SessionTokens): Promise<SessionTokens | null> {
  try {
    const refreshRes = await fetch(`${process.env.API_BASE_URL}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        accessToken: session.accessToken,
        refreshToken: session.refreshToken,
      }),
      cache: "no-store",
    });
    if (!refreshRes.ok) {
      return null;
    }

    const parsed = refreshedTokensSchema.safeParse(await refreshRes.json());
    if (!parsed.success) {
      return null;
    }

    return {
      accessToken: parsed.data.accessToken,
      refreshToken: parsed.data.refreshToken,
      refreshExpiresAt: new Date(parsed.data.refreshTokenExpires),
    };
  } catch {
    return null;
  }
}

/**
 * Exchange the stored refresh token for a new access token and store the new tokens in the
 * session cookies. Returns the new access token, or null when the session cannot be refreshed.
 * Concurrent calls with the same refresh token share one exchange.
 */
export async function refreshSessionTokens(): Promise<string | null> {
  const session = await getSessionTokens();
  if (!session) {
    return null;
  }

  const key = session.refreshToken;
  let pending = sessionRefreshes.get(key);
  if (!pending) {
    const exchange = exchangeRefreshToken(session);
    pending = exchange;
    sessionRefreshes.set(key, exchange);
    // Keep only a successful result for reuse; a failed (possibly transient) exchange is forgotten
    // at once so the next request tries again instead of being logged out for the reuse window.
    void exchange.then((tokens) => {
      if (!tokens) {
        if (sessionRefreshes.get(key) === exchange) sessionRefreshes.delete(key);
        return;
      }
      setTimeout(() => {
        if (sessionRefreshes.get(key) === exchange) sessionRefreshes.delete(key);
      }, REFRESH_REUSE_MS).unref?.();
    });
  }

  const tokens = await pending;
  if (!tokens) {
    return null;
  }

  await setSessionCookies(tokens);
  return tokens.accessToken;
}

/**
 * Clear the session cookies after a failed refresh, but only when they still hold the refresh token
 * that failed. If another request already stored newer tokens, keep them.
 */
export async function clearSessionCookiesIfUnchanged(failedRefreshToken: string): Promise<void> {
  const current = await getSessionTokens();
  if (!current || current.refreshToken === failedRefreshToken) {
    await clearSessionCookies();
  }
}
