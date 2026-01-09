import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";
import {
  clearSessionCookies,
  getSessionTokens,
  setSessionCookies,
} from "../lib/auth-helpers";

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
  // Register via BFF - creates Identity + EventSource and signs in
  register: publicProcedure
    .input(
      z.object({
        email: z.string().email(),
        password: z.string().min(8),
        displayName: z.string().min(1).max(200).optional(),
      })
    )
    .mutation(async ({ input }) => {
      const registerRes = await fetch(`${process.env.API_BASE_URL}/auth/register`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          email: input.email,
          password: input.password,
          displayName: input.displayName ?? null,
        }),
      });

      if (!registerRes.ok) {
        const payload = await registerRes.json().catch(() => null);
        const message =
          payload?.message || payload?.Message || payload?.errors?.join(", ") || "Registration failed";
        throw new Error(message);
      }

      const loginRes = await fetch(`${process.env.API_BASE_URL}/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          email: input.email,
          password: input.password,
          useCookies: false,
        }),
      });

      if (!loginRes.ok) {
        throw new Error("Registration succeeded but auto login failed");
      }

      const tokenData = tokenResponseSchema.parse(await loginRes.json());

      await setSessionCookies({
        accessToken: tokenData.accessToken,
        refreshToken: tokenData.refreshToken,
        refreshExpiresAt: new Date(tokenData.refreshTokenExpires),
      });

      const userRes = await fetch(`${process.env.API_BASE_URL}/auth/me`, {
        headers: { Authorization: `Bearer ${tokenData.accessToken}` },
      });

      if (userRes.ok) {
        return userInfoSchema.parse(await userRes.json());
      }

      return {
        id: "",
        email: input.email,
        displayName: input.displayName ?? null,
        roles: [] as string[],
        isAuthenticated: true,
      };
    }),
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

      await setSessionCookies({
        accessToken: tokenData.accessToken,
        refreshToken: tokenData.refreshToken,
        refreshExpiresAt: new Date(tokenData.refreshTokenExpires),
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
    await clearSessionCookies();

    return { success: true };
  }),

  // Get current auth status
  status: publicProcedure.query(async () => {
    const session = await getSessionTokens();
    if (!session) {
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
        await setSessionCookies({
          accessToken: newTokens.accessToken,
          refreshToken: newTokens.refreshToken,
          refreshExpiresAt: new Date(newTokens.refreshTokenExpires),
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
      await clearSessionCookies();
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
