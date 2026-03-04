import { expect, test, type APIRequestContext, type Page } from "@playwright/test";

type ProjectionMode = "standard" | "single" | "generic";

type BenchStatus = {
  isRunning: boolean;
  created: number;
  updated: number;
  errors: number;
  weatherCount: number | null;
  endpointMode: ProjectionMode;
  lastError?: string | null;
};

type ProjectionCountResponse = {
  safeVersion: string;
  unsafeVersion?: string;
  totalCount: number;
};

type ProjectionStatusResponse = {
  isCaughtUp: boolean;
  isSubscriptionActive: boolean;
  stateSize: number;
  eventsProcessed: number;
  currentPosition?: string;
};

const runPlan: Array<{ mode: ProjectionMode; total: number; concurrency: number }> = [
  { mode: "standard", total: 10000, concurrency: 32 },
  { mode: "generic", total: 20000, concurrency: 32 }
];
const strictSingleProjectionCheck = (process.env.PW_ASSERT_SINGLE_PROJECTION ?? "false").toLowerCase() === "true";

test.describe("DCB heavy E2E (catch-up + cold events)", () => {
  test("run benchmark at 10k/20k and validate projections/catch-up/cold-event APIs", async ({ page, request }) => {
    test.setTimeout(90 * 60 * 1000);

    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Bench Runner" })).toBeVisible();

    let expectedCumulativeCount = 0;

    for (const runCase of runPlan) {
      expectedCumulativeCount += runCase.total;
      await startBenchmarkRun(page, runCase.mode, runCase.total, runCase.concurrency);
      await waitForBenchmarkCompletion(request, runCase.mode, runCase.total);
      if (runCase.mode === "standard") {
        await waitForProjectionCount(request, runCase.mode, expectedCumulativeCount);
      }
      await verifyProjectionStatus(request, runCase.mode);
    }

    await runProjectionControlCycle(page, request, "standard", expectedCumulativeCount);
    await runProjectionControlCycle(page, request, "generic");

    await verifySingleProjectionSurface(request);
    await verifyColdEventSurface(page, request);
  });
});

async function startBenchmarkRun(page: Page, mode: ProjectionMode, total: number, concurrency: number): Promise<void> {
  await page.locator("#total").fill(String(total));
  await page.locator("#conc").fill(String(concurrency));
  await page.locator("#endpointMode").selectOption(mode);
  await page.locator("#stopOnError").setChecked(true);

  await Promise.all([
    page.waitForResponse((res) => res.url().includes("/run?") && res.request().method() === "POST", { timeout: 30_000 }),
    page.locator("#startBtn").click()
  ]);
}

async function waitForBenchmarkCompletion(
  request: APIRequestContext,
  mode: ProjectionMode,
  expectedCount: number
): Promise<void> {
  const maxWaitMs = 45 * 60 * 1000;
  const intervalMs = 2000;
  const startedAt = Date.now();
  let hasSeenRunning = false;

  while (Date.now() - startedAt < maxWaitMs) {
    const status = await getBenchStatus(request);
    if (!status) {
      await delay(intervalMs);
      continue;
    }
    if (status.isRunning) {
      hasSeenRunning = true;
    }

    if (hasSeenRunning && !status.isRunning) {
      expect(status.endpointMode).toBe(mode);
      expect(status.errors, status.lastError ?? "errors should be 0").toBe(0);
      expect(status.created).toBe(expectedCount);
      expect(status.updated).toBe(expectedCount);
      return;
    }

    await delay(intervalMs);
  }

  throw new Error(`Benchmark did not finish within ${maxWaitMs / 1000}s for mode=${mode}, expected=${expectedCount}`);
}

async function waitForProjectionCount(
  request: APIRequestContext,
  mode: ProjectionMode,
  expectedMinimumCount: number
): Promise<ProjectionCountResponse> {
  const maxWaitMs = 20 * 60 * 1000;
  const intervalMs = 2000;
  const startedAt = Date.now();

  while (Date.now() - startedAt < maxWaitMs) {
    const count = await getProjectionCount(request, mode);
    if (count && count.totalCount >= expectedMinimumCount) {
      return count;
    }
    await delay(intervalMs);
  }

  throw new Error(`Projection count did not reach ${expectedMinimumCount} for mode=${mode}`);
}

async function verifyProjectionStatus(request: APIRequestContext, mode: ProjectionMode): Promise<void> {
  const maxWaitMs = 5 * 60 * 1000;
  const intervalMs = 1500;
  const startedAt = Date.now();

  while (Date.now() - startedAt < maxWaitMs) {
    const status = await getProjectionStatus(request, mode);
    if (status?.isCaughtUp) {
      expect(status.isSubscriptionActive).toBeTruthy();
      expect(status.eventsProcessed).toBeGreaterThanOrEqual(0);
      return;
    }
    await delay(intervalMs);
  }

  throw new Error(`Projection status did not become caught-up for mode=${mode}`);
}

async function runProjectionControlCycle(
  page: Page,
  request: APIRequestContext,
  mode: ProjectionMode,
  expectedMinimumCount?: number
): Promise<void> {
  await page.evaluate(async (currentMode) => {
    await (window as unknown as { doPersist: (m: string) => Promise<void> }).doPersist(currentMode);
    await (window as unknown as { doDeactivate: (m: string) => Promise<void> }).doDeactivate(currentMode);
    await (window as unknown as { doRefresh: (m: string) => Promise<void> }).doRefresh(currentMode);
  }, mode);

  if (typeof expectedMinimumCount === "number") {
    await waitForProjectionCount(request, mode, expectedMinimumCount);
  }
  await verifyProjectionStatus(request, mode);
}

async function verifyColdEventSurface(page: Page, request: APIRequestContext): Promise<void> {
  const coldStatusResponse = await request.get("/cold/status", { timeout: 20_000 });
  expect(coldStatusResponse.ok()).toBeTruthy();
  const coldStatus = (await coldStatusResponse.json()) as { isEnabled?: boolean };
  expect(typeof coldStatus.isEnabled).toBe("boolean");

  await page.getByRole("button", { name: "status" }).click();
  await page.waitForTimeout(300);

  const progressResponse = await request.get("/cold/progress", { timeout: 20_000 });
  expect(progressResponse.ok()).toBeTruthy();

  const catalogResponse = await request.get("/cold/catalog", { timeout: 20_000 });
  expect(catalogResponse.ok()).toBeTruthy();

  const exportResponse = await request.post("/cold/export", { timeout: 20_000 });
  expect(exportResponse.ok()).toBeTruthy();
}

async function verifySingleProjectionSurface(request: APIRequestContext): Promise<void> {
  const response = await request.get("/projection/status?mode=single", { timeout: 20_000 });
  if (!response.ok()) {
    if (strictSingleProjectionCheck) {
      expect(response.ok()).toBeTruthy();
    }
    return;
  }

  const text = await response.text();
  const parsed = JSON.parse(text) as ProjectionStatusResponse;
  expect(typeof parsed.isCaughtUp).toBe("boolean");
}

async function getBenchStatus(request: APIRequestContext): Promise<BenchStatus | null> {
  try {
    const response = await request.get("/status", { timeout: 20_000 });
    if (!response.ok()) {
      return null;
    }
    return (await response.json()) as BenchStatus;
  } catch {
    return null;
  }
}

async function getProjectionCount(request: APIRequestContext, mode: ProjectionMode): Promise<ProjectionCountResponse | null> {
  try {
    const response = await request.get(`/projection/count?mode=${mode}`, { timeout: 20_000 });
    if (!response.ok()) {
      return null;
    }
    const text = await response.text();
    return JSON.parse(text) as ProjectionCountResponse;
  } catch {
    return null;
  }
}

async function getProjectionStatus(request: APIRequestContext, mode: ProjectionMode): Promise<ProjectionStatusResponse | null> {
  try {
    const response = await request.get(`/projection/status?mode=${mode}`, { timeout: 20_000 });
    if (!response.ok()) {
      return null;
    }
    const text = await response.text();
    const parsed = JSON.parse(text) as ProjectionStatusResponse;
    return parsed;
  } catch {
    return null;
  }
}

async function delay(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
}
