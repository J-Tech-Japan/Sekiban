"use client";

import { NavMenu } from "./nav-menu";
import { trpc } from "@/lib/trpc";
import Link from "next/link";

export function MainLayout({ children }: { children: React.ReactNode }) {
  const { data: authStatus, isLoading: authLoading, isFetching } = trpc.auth.status.useQuery(undefined, {
    staleTime: 0,
    refetchOnMount: "always",
  });
  // Show loading while fetching if not yet authenticated (prevents flash of "Please Login")
  const showLoading = authLoading || (isFetching && !authStatus?.isAuthenticated);
  const isAuthenticated = authStatus?.isAuthenticated ?? false;
  return (
    <div className="flex min-h-screen bg-background">
      {/* Sidebar */}
      <aside className="fixed left-0 top-0 z-40 h-screen w-64 bg-sidebar">
        <div className="flex h-full flex-col">
          {/* Logo */}
          <div className="flex h-16 items-center gap-3 border-b border-white/10 px-4">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-sidebar-accent">
              <svg className="h-5 w-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
              </svg>
            </div>
            <div>
              <h1 className="text-sm font-semibold text-sidebar-foreground">Sekiban DCB</h1>
              <p className="text-xs text-sidebar-foreground/60">Orleans Platform</p>
            </div>
          </div>

          {/* Navigation */}
          <div className="flex-1 overflow-y-auto py-4 scrollbar-thin">
            <NavMenu />
          </div>

          {/* Footer */}
          <div className="border-t border-white/10 p-4">
            {showLoading ? (
              <div className="flex items-center gap-3 rounded-lg bg-white/5 px-3 py-2">
                <div className="h-8 w-8 rounded-full bg-sidebar-accent/20 animate-pulse" />
                <div className="flex-1 min-w-0">
                  <div className="h-4 w-20 bg-sidebar-accent/20 rounded animate-pulse" />
                  <div className="h-3 w-32 bg-sidebar-accent/10 rounded mt-1 animate-pulse" />
                </div>
              </div>
            ) : isAuthenticated && authStatus ? (
              <div className="flex items-center gap-3 rounded-lg bg-white/5 px-3 py-2">
                <div className="flex h-8 w-8 items-center justify-center rounded-full bg-sidebar-accent/20 text-sidebar-accent">
                  <span className="text-sm font-medium">
                    {(authStatus.displayName || authStatus.email || "U").charAt(0).toUpperCase()}
                  </span>
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-sidebar-foreground truncate">
                    {authStatus.displayName || authStatus.email?.split("@")[0] || "User"}
                  </p>
                  <p className="text-xs text-sidebar-foreground/60 truncate">{authStatus.email}</p>
                </div>
              </div>
            ) : (
              <Link
                href="/login"
                className="flex items-center gap-3 rounded-lg bg-sidebar-accent/20 px-3 py-2 hover:bg-sidebar-accent/30 transition-colors"
              >
                <div className="flex h-8 w-8 items-center justify-center rounded-full bg-sidebar-accent/30 text-sidebar-accent">
                  <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11 16l-4-4m0 0l4-4m-4 4h14m-5 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h7a3 3 0 013 3v1" />
                  </svg>
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-sidebar-foreground">Please Login</p>
                  <p className="text-xs text-sidebar-foreground/60">Click to sign in</p>
                </div>
              </Link>
            )}
          </div>
        </div>
      </aside>

      {/* Main Content */}
      <div className="flex-1 pl-64">
        {/* Header */}
        <header className="sticky top-0 z-30 flex h-16 items-center gap-4 border-b bg-card px-6 shadow-sm">
          <div className="flex flex-1 items-center gap-4">
            <div className="relative flex-1 max-w-md">
              <svg
                className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
                />
              </svg>
              <input
                type="search"
                placeholder="Search..."
                className="h-10 w-full rounded-lg border bg-background pl-10 pr-4 text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
              />
            </div>
          </div>

          <div className="flex items-center gap-3">
            <button className="relative flex h-9 w-9 items-center justify-center rounded-lg text-muted-foreground hover:bg-accent hover:text-foreground transition-colors">
              <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
              </svg>
              <span className="absolute -top-0.5 -right-0.5 flex h-4 w-4 items-center justify-center rounded-full bg-destructive text-[10px] font-medium text-destructive-foreground">3</span>
            </button>
            <button className="flex h-9 w-9 items-center justify-center rounded-lg text-muted-foreground hover:bg-accent hover:text-foreground transition-colors">
              <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
            </button>
          </div>
        </header>

        {/* Page Content */}
        <main className="p-6">{children}</main>
      </div>
    </div>
  );
}
