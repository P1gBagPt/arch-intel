import { expect, test } from "@playwright/test";
import { API_ORIGIN, mockApi } from "./fixtures/api";

test.beforeEach(async ({ page }) => {
  await mockApi(page, API_ORIGIN);
});

// Regression-guards the fixture's affected-category checklist (06-dashboard.md §10.2) — mirrors
// the real OrderRepository/IOrderRepository shape confirmed against the live API this session:
// IOrderRepository has 2 Repository implementers plus 1 MediatRHandler injecting it.
test("shows the affected-category checklist for a known target", async ({ page }) => {
  await page.goto("/impact/iorder-repository");

  await expect(page.getByRole("heading", { name: "Impact of changing IOrderRepository" })).toBeVisible();
  await expect(page.getByText("3 affected components", { exact: false })).toBeVisible();

  await expect(page.getByText("Repository (2)", { exact: false })).toBeVisible();
  await expect(page.getByText("MediatRHandler (1)", { exact: false })).toBeVisible();

  await expect(page.getByRole("link", { name: "OrderRepository", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "FakeOrderRepository" })).toBeVisible();
  await expect(page.getByRole("link", { name: "CreateOrderCommandHandler" })).toBeVisible();
});

test("renders the graph highlight for the target", async ({ page }) => {
  await page.goto("/impact/iorder-repository");
  // The highlight canvas mounts once useDependencyGraph + useImpactAnalysis both resolve — wait
  // for its container rather than a fixed sleep.
  await expect(page.locator("canvas").first()).toBeVisible();
});

test("links back to the Dependency Graph centered on the target", async ({ page }) => {
  await page.goto("/impact/iorder-repository");
  await expect(page.getByRole("link", { name: "View in Dependency Graph" })).toHaveAttribute(
    "href",
    "/graph/iorder-repository",
  );
});

test("exports the impact subgraph as Mermaid", async ({ page }) => {
  await page.goto("/impact/iorder-repository");
  await page.getByRole("button", { name: "Export as Mermaid" }).click();
  await expect(page.getByRole("heading", { name: "Mermaid export" })).toBeVisible();
  await expect(page.getByText("graph TD", { exact: false })).toBeVisible();
});

test("shows a 404-derived empty state for an unknown target", async ({ page }) => {
  await page.goto("/impact/does-not-exist");
  await expect(page.getByText(/Failed to analyze impact/i)).toBeVisible();
});

test("links to the AI Planner pre-filled with an implementation-plan prompt for the target", async ({ page }) => {
  await page.goto("/impact/iorder-repository");
  await expect(page.getByRole("link", { name: "Plan this change" })).toHaveAttribute(
    "href",
    "/planner?kind=implementation-plan&prompt=Implement%20changes%20to%20IOrderRepository",
  );

  await page.getByRole("link", { name: "Plan this change" }).click();
  await expect(page).toHaveURL(/\/planner\?/);
  await expect(page.getByRole("button", { name: "Implementation Plan" })).toHaveClass(/bg-accent/);
  await expect(page.getByPlaceholder("Describe the change you want to implement…")).toHaveValue(
    "Implement changes to IOrderRepository",
  );
});
