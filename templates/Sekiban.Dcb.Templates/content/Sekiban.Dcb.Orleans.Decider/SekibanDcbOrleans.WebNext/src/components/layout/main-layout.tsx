"use client";

import { NavMenu } from "./nav-menu";

export function MainLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen">
      <aside className="w-64 border-r bg-card p-4">
        <div className="mb-6">
          <h1 className="text-xl font-bold">Sekiban DCB Orleans</h1>
        </div>
        <NavMenu />
      </aside>
      <main className="flex-1 p-6">{children}</main>
    </div>
  );
}
