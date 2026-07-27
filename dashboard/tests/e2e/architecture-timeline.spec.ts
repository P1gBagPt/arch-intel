import { expect, test } from "@playwright/test";
import { API_ORIGIN, mockApi } from "./fixtures/api";

test.beforeEach(async ({ page }) => {
  await mockApi(page, API_ORIGIN);
});

// Delta detection across a real 15s poll tick is covered by manual verification against the live
// backend (a real re-scan produced a genuine "+1 class" entry) rather than here — simulating the
// interval in e2e would make the suite slow and timer-flaky for no extra confidence.
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
