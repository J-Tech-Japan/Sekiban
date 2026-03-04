import { defineConfig } from "@playwright/test";

const benchBaseUrl = process.env.BENCH_BASE_URL ?? "http://127.0.0.1:5411";
const appHostProjectPath = process.env.DCB_APPHOST_PROJECT_PATH
  ?? "../DcbOrleans.AppHost/DcbOrleans.AppHost.csproj";
const derivedBenchPort = (() => {
  if (process.env.BENCH_HTTP_PORT) {
    return process.env.BENCH_HTTP_PORT;
  }

  const parsed = new URL(benchBaseUrl);
  if (parsed.port) {
    return parsed.port;
  }
  return parsed.protocol === "https:" ? "443" : "80";
})();

export default defineConfig({
  testDir: "./tests",
  fullyParallel: false,
  workers: 1,
  timeout: 90 * 60 * 1000,
  expect: {
    timeout: 30 * 1000
  },
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: benchBaseUrl,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure"
  },
  webServer: {
    command: `dotnet run --project ${appHostProjectPath}`,
    cwd: ".",
    url: `${benchBaseUrl}/status`,
    timeout: 12 * 60 * 1000,
    reuseExistingServer: true,
    stdout: "pipe",
    stderr: "pipe",
    env: {
      ...process.env,
      BENCH_HTTP_PORT: derivedBenchPort
    }
  }
});
