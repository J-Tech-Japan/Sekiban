import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";
import { cookies } from "next/headers";
import { tokenStore, SESSION_COOKIE_NAME } from "../lib/auth-helpers";

// Helper to generate a simple session ID
function generateSessionId(): string {
  return `session_${Date.now()}_${Math.random().toString(36).substring(2, 15)}`;
}

// Schema for user info response
const userInfoSchema = z.object({
  id: z.string(),
  email: z.string(),
  displayName: z.string().nullable(),
  roles: z.array(z.string()),
  isAuthenticated: z.boolean(),
});

// Schema for token response
const tokenResponseSchema = z.object({
  accessToken: z.string(),
  refreshToken: z.string(),
  accessTokenExpires: z.string(),
  refreshTokenExpires: z.string(),
});

export const authRouter = router({
  // Login via BFF - stores JWT server-side
  login: publicProcedure
    .input(
      z.object({
        email: z.string().email(),
        password: z.string().min(1),
      })
    )
    .mutation(async ({ input }) => {
      // Call API server for JWT login
      const res = await fetch(`${process.env.API_BASE_URL}/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          email: input.email,
          password: input.password,
          useCookies: false, // Request JWT tokens
        }),
      });

      if (!res.ok) {
        if (res.status === 401) {
          throw new Error("Invalid email or password");
        }
        if (res.status === 423) {
          throw new Error("Account is locked. Please try again later.");
        }
        throw new Error("Login failed");
      }

      const tokenData = tokenResponseSchema.parse(await res.json());

      // Generate session ID and store tokens server-side
      const sessionId = generateSessionId();
      tokenStore.set(sessionId, {
        accessToken: tokenData.accessToken,
        refreshToken: tokenData.refreshToken,
        expiresAt: new Date(tokenData.refreshTokenExpires),
      });

      // Set session cookie (not the JWT itself!)
      const cookieStore = await cookies();
      cookieStore.set(SESSION_COOKIE_NAME, sessionId, {
        httpOnly: true,
        secure: process.env.NODE_ENV === "production",
        sameSite: "lax",
        maxAge: 60 * 60 * 24 * 7, // 7 days
        path: "/",
      });

      // Return user info (extracted from token or fetched)
      const userRes = await fetch(`${process.env.API_BASE_URL}/auth/me`, {
        headers: { Authorization: `Bearer ${tokenData.accessToken}` },
      });

      if (userRes.ok) {
        return userInfoSchema.parse(await userRes.json());
      }

      // Fallback minimal user info
      return {
        id: "",
        email: input.email,
        displayName: null,
        roles: [] as string[],
        isAuthenticated: true,
      };
    }),

  // Logout - clear server-side tokens
  logout: publicProcedure.mutation(async () => {
    const cookieStore = await cookies();
    const sessionId = cookieStore.get(SESSION_COOKIE_NAME)?.value;

    if (sessionId) {
      // Remove tokens from store
      tokenStore.delete(sessionId);
      // Clear session cookie
      cookieStore.delete(SESSION_COOKIE_NAME);
    }

    return { success: true };
  }),

  // Get current auth status
  status: publicProcedure.query(async () => {
    const cookieStore = await cookies();
    const sessionId = cookieStore.get(SESSION_COOKIE_NAME)?.value;

    if (!sessionId) {
      return {
        id: "",
        email: "",
        displayName: null,
        roles: [] as string[],
        isAuthenticated: false,
      };
    }

    const session = tokenStore.get(sessionId);
    if (!session || new Date() > session.expiresAt) {
      // Session expired, clean up
      if (session) tokenStore.delete(sessionId);
      cookieStore.delete(SESSION_COOKIE_NAME);
      return {
        id: "",
        email: "",
        displayName: null,
        roles: [] as string[],
        isAuthenticated: false,
      };
    }

    // Fetch current user info using stored token
    try {
      const res = await fetch(`${process.env.API_BASE_URL}/auth/me`, {
        headers: { Authorization: `Bearer ${session.accessToken}` },
      });

      if (res.ok) {
        return userInfoSchema.parse(await res.json());
      }

      // Token might be expired, try to refresh
      const refreshRes = await fetch(`${process.env.API_BASE_URL}/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          accessToken: session.accessToken,
          refreshToken: session.refreshToken,
        }),
      });

      if (refreshRes.ok) {
        const newTokens = tokenResponseSchema.parse(await refreshRes.json());
        // Update stored tokens
        tokenStore.set(sessionId, {
          accessToken: newTokens.accessToken,
          refreshToken: newTokens.refreshToken,
          expiresAt: new Date(newTokens.refreshTokenExpires),
        });

        // Retry getting user info
        const retryRes = await fetch(`${process.env.API_BASE_URL}/auth/me`, {
          headers: { Authorization: `Bearer ${newTokens.accessToken}` },
        });

        if (retryRes.ok) {
          return userInfoSchema.parse(await retryRes.json());
        }
      }

      // Failed to refresh, clean up
      tokenStore.delete(sessionId);
      cookieStore.delete(SESSION_COOKIE_NAME);
      return {
        id: "",
        email: "",
        displayName: null,
        roles: [] as string[],
        isAuthenticated: false,
      };
    } catch {
      return {
        id: "",
        email: "",
        displayName: null,
        roles: [] as string[],
        isAuthenticated: false,
      };
    }
  }),
});
