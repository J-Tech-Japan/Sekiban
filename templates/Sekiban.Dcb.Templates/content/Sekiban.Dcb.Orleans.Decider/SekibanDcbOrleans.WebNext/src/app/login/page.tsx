"use client";

import { useState } from "react";
import { trpc } from "@/lib/trpc";
import { Card, CardContent, CardDescription, CardHeader, CardTitle, CardFooter } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export default function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  const utils = trpc.useUtils();
  const { data: authStatus, isLoading: isStatusLoading } = trpc.auth.status.useQuery();

  const loginMutation = trpc.auth.login.useMutation({
    onSuccess: async () => {
      setError(null);
      await utils.auth.status.invalidate();
    },
    onError: (err) => {
      setError(err.message);
    },
  });

  const logoutMutation = trpc.auth.logout.useMutation({
    onSuccess: () => {
      utils.auth.status.invalidate();
    },
  });

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    loginMutation.mutate({ email, password });
  };

  const handleLogout = () => {
    logoutMutation.mutate();
  };

  const isAuthenticated = authStatus?.isAuthenticated ?? false;
  const isLoading = loginMutation.isPending || logoutMutation.isPending || isStatusLoading;

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div>
        <h1 className="text-2xl font-semibold text-foreground">Login</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Authenticate to access protected features
        </p>
      </div>

      <div className="max-w-md mx-auto">
        <Card>
          <CardHeader>
            <CardTitle>{isAuthenticated ? "Welcome!" : "Sign In"}</CardTitle>
            <CardDescription>
              {isAuthenticated
                ? `You are logged in as ${authStatus?.displayName || authStatus?.email}`
                : "Enter your credentials to continue"}
            </CardDescription>
          </CardHeader>
          <CardContent>
            {error && (
              <div className="mb-4 p-3 bg-destructive/10 border border-destructive/20 rounded-lg text-destructive text-sm">
                {error}
              </div>
            )}

            {isAuthenticated ? (
              <div className="space-y-4">
                <div className="p-4 bg-muted rounded-lg space-y-2">
                  <p className="text-sm">
                    <span className="font-medium">Email:</span> {authStatus?.email}
                  </p>
                  <p className="text-sm">
                    <span className="font-medium">Display Name:</span> {authStatus?.displayName || "N/A"}
                  </p>
                  <p className="text-sm">
                    <span className="font-medium">Roles:</span>{" "}
                    {authStatus?.roles?.join(", ") || "None"}
                  </p>
                </div>
                <Button
                  variant="destructive"
                  className="w-full"
                  onClick={handleLogout}
                  disabled={isLoading}
                >
                  {isLoading ? (
                    <>
                      <svg className="animate-spin -ml-1 mr-2 h-4 w-4" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                      </svg>
                      Logging out...
                    </>
                  ) : (
                    "Logout"
                  )}
                </Button>
              </div>
            ) : (
              <form onSubmit={handleLogin} className="space-y-4">
                <div className="space-y-2">
                  <label htmlFor="email" className="text-sm font-medium">
                    Email
                  </label>
                  <Input
                    id="email"
                    type="email"
                    placeholder="Enter your email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    required
                  />
                </div>
                <div className="space-y-2">
                  <label htmlFor="password" className="text-sm font-medium">
                    Password
                  </label>
                  <Input
                    id="password"
                    type="password"
                    placeholder="Enter your password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                  />
                </div>
                <Button type="submit" className="w-full" disabled={isLoading}>
                  {isLoading ? (
                    <>
                      <svg className="animate-spin -ml-1 mr-2 h-4 w-4" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                      </svg>
                      Signing in...
                    </>
                  ) : (
                    "Sign In"
                  )}
                </Button>
              </form>
            )}
          </CardContent>
          {!isAuthenticated && (
            <CardFooter className="flex flex-col">
              <div className="w-full p-4 bg-muted/50 rounded-lg">
                <h3 className="text-sm font-medium mb-3">Quick Login (Sample Accounts):</h3>
                <div className="grid grid-cols-2 gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      setEmail("user1@example.com");
                      setPassword("Sekiban1234%");
                    }}
                  >
                    User 1
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      setEmail("user2@example.com");
                      setPassword("Sekiban1234%");
                    }}
                  >
                    User 2
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      setEmail("user3@example.com");
                      setPassword("Sekiban1234%");
                    }}
                  >
                    User 3
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="font-medium"
                    onClick={() => {
                      setEmail("admin@example.com");
                      setPassword("Sekiban1234%");
                    }}
                  >
                    Admin
                  </Button>
                </div>
                <p className="text-xs text-muted-foreground mt-2 text-center">
                  Click a button to fill credentials, then press Sign In
                </p>
              </div>
            </CardFooter>
          )}
        </Card>
      </div>
    </div>
  );
}
