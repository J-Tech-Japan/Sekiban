import { defineConfig } from "@playwright/test";

const benchBaseUrl = process.env.BENCH_BASE_URL ?? "http://127.0.0.1:5411";
const appHostProjectPath = process.env.DCB_APPHOST_PROJECT_PATH
  ?? "../DcbOrleans.AppHost/DcbOrleans.AppHost.csproj";

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
    command: `BENCH_HTTP_PORT=5411 dotnet run --project ${appHostProjectPath}`,
    cwd: ".",
    url: `${benchBaseUrl}/status`,
    timeout: 12 * 60 * 1000,
    reuseExistingServer: true,
    stdout: "pipe",
    stderr: "pipe"
  }
});
