"use client";

import { Button } from "@/components/ui/button";

export type QueryMode = "memory" | "materialized";

interface QueryModeToggleProps {
  value: QueryMode;
  onChange: (mode: QueryMode) => void;
  className?: string;
}

/**
 * Lets the user pick between the in-memory MultiProjection (Orleans grain state)
 * and the SQL-backed materialized view as the source for list queries.
 *
 * The two views read the same event stream but expose different latency,
 * freshness and query-shape characteristics, so making the selection visible
 * helps when comparing them side by side.
 */
export function QueryModeToggle({ value, onChange, className }: QueryModeToggleProps) {
  return (
    <div className={`inline-flex items-center gap-2 ${className ?? ""}`}>
      <span className="text-sm text-muted-foreground">Query source:</span>
      <div className="inline-flex rounded-md border bg-background p-0.5">
        <Button
          type="button"
          size="sm"
          variant={value === "memory" ? "default" : "ghost"}
          onClick={() => onChange("memory")}
        >
          Memory projection
        </Button>
        <Button
          type="button"
          size="sm"
          variant={value === "materialized" ? "default" : "ghost"}
          onClick={() => onChange("materialized")}
        >
          Materialized view
        </Button>
      </div>
    </div>
  );
}
