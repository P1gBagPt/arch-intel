import { defineConfig, devices } from "@playwright/test";

// 06-dashboard.md §10.2/§10.3: specs target the "one canonical sample architecture" fixture
// (tests/e2e/fixtures/api.ts) via page.route() interception rather than a live backend — the
// dashboard's api-client always calls an absolute NEXT_PUBLIC_API_URL (not same-origin), so
// nothing needs to actually be listening on that port for these tests; Playwright intercepts the
// request before it reaches the network. Only the Next.js dev server itself needs to run.
export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: "list",
  use: {
    baseURL: "http://localhost:3000",
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: {
    command: "npm run dev",
    url: "http://localhost:3000",
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});
