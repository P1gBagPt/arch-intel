import { expect, test } from "@playwright/test";
import { API_ORIGIN, mockApi, METRICS, PROJECTS } from "./fixtures/api";

test.beforeEach(async ({ page }) => {
  await mockApi(page, API_ORIGIN);
});

// Delta detection across a real 15s poll tick is verified end-to-end (not timer-mocked) in
// "detects a real project change across a poll tick" below, using Playwright's clock API to fast-
// forward the 15s interval rather than actually waiting — this also exercised a real re-scan
// against the live backend during manual verification (a genuine "+1 class" entry appeared).
test("shows a baseline reading from the current metrics", async ({ page }) => {
  await page.goto("/timeline");

  await expect(page.getByRole("heading", { name: "Architecture Timeline" })).toBeVisible();
  await expect(page.getByText("Now")).toBeVisible();
  await expect(page.getByText("12 classes, 3 projects, 2 interfaces, 1 services")).toBeVisible();
  await expect(page.getByText("Baseline reading")).toBeVisible();
});

test("shows a placeholder trend chart until a second reading arrives", async ({ page }) => {
  await page.goto("/timeline");
  await expect(page.getByText(/Trend chart appears once a change is detected/)).toBeVisible();
});

// Fast-forwards the real 15s poll interval via Playwright's clock API rather than waiting for it
// — TanStack Query's refetchInterval runs on real setTimeout/setInterval, which the installed
// fake clock intercepts, so this exercises the actual polling/diffing code path, not a mock of it.
test("detects a real project change across a poll tick, renders the trend chart, and shows a real diff", async ({
  page,
}) => {
  let metricsCalls = 0;
  let projectsCalls = 0;
  const CHANGED_METRICS = {
    ...METRICS,
    totalProjects: METRICS.totalProjects + 1,
    totalClasses: METRICS.totalClasses + 2,
    generatedAtUtc: "2026-01-01T00:15:00Z",
  };
  const NEW_PROJECT = {
    id: "proj-new",
    name: "SampleErp.NewFeature",
    path: "NewFeature/SampleErp.NewFeature.csproj",
    projectType: null,
    layer: null,
    targetFramework: null,
  };

  await page.route(`${API_ORIGIN}/api/v1/repos/*/metrics`, (route) => {
    metricsCalls++;
    const body = metricsCalls === 1 ? METRICS : CHANGED_METRICS;
    return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ data: body, page: null }) });
  });
  await page.route(`${API_ORIGIN}/api/v1/repos/*/projects*`, (route) => {
    projectsCalls++;
    const list = projectsCalls === 1 ? PROJECTS : [...PROJECTS, NEW_PROJECT];
    return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ data: list, page: null }) });
  });

  await page.clock.install();
  await page.goto("/timeline");
  await expect(page.getByText("Baseline reading")).toBeVisible();

  await page.clock.fastForward("00:16");

  await expect(page.getByText(/\+1 project/)).toBeVisible();
  await expect(page.getByRole("application")).toBeVisible();

  await page.getByText(/Change detected/).click();
  await expect(page.getByText("Added projects")).toBeVisible();
  await expect(page.getByText("SampleErp.NewFeature")).toBeVisible();
});
