import { expect, test } from "@playwright/test";
import { API_ORIGIN, mockApi } from "./fixtures/api";

test.beforeEach(async ({ page }) => {
  await mockApi(page, API_ORIGIN);
});

test("shows a treemap by default with a real cell per project", async ({ page }) => {
  await page.goto("/coupling");

  const cells = page.locator('g[role="button"]');
  await expect(cells).toHaveCount(3);
  await expect(page.getByRole("button", { name: /SampleErp.Infrastructure: Highly coupled/ })).toBeVisible();
  await expect(page.getByRole("button", { name: /SampleErp.Domain: Stable/ })).toBeVisible();
});

test("shows the coupling grid sorted by instability with band labels", async ({ page }) => {
  await page.goto("/coupling");
  await page.getByRole("button", { name: "Grid", exact: true }).click();

  const cards = page.locator("button", { hasText: "Instability" });
  await expect(cards).toHaveCount(3);
  // Sorted descending by instability: Infrastructure (1.0) → Application (0.5) → Domain (0.2).
  await expect(cards.nth(0)).toContainText("SampleErp.Infrastructure");
  await expect(cards.nth(0)).toContainText("Highly coupled");
  await expect(cards.nth(2)).toContainText("SampleErp.Domain");
  await expect(cards.nth(2)).toContainText("Stable");
});

test("shows the circular dependency banner with a working link", async ({ page }) => {
  await page.goto("/coupling");
  await expect(page.getByText("1 circular dependency detected")).toBeVisible();
  await expect(page.getByRole("link", { name: "SampleErp.Application" }).first()).toHaveAttribute(
    "href",
    "/graph/proj-application",
  );

  await page.getByRole("button", { name: "Dismiss" }).click();
  await expect(page.getByText("1 circular dependency detected")).not.toBeVisible();
});

test("opens the detail panel with real numbers and a graph link", async ({ page }) => {
  await page.goto("/coupling");
  await page.getByRole("button", { name: "Grid", exact: true }).click();
  await page.locator("button", { hasText: "SampleErp.Infrastructure" }).click();

  await expect(page.getByText("Coupling details")).toBeVisible();
  await expect(page.getByText("Afferent coupling")).toBeVisible();
  await expect(page.getByText("0", { exact: true })).toBeVisible();
  await expect(page.getByText("5", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "View in Dependency Graph" })).toHaveAttribute(
    "href",
    "/graph/proj-infrastructure",
  );
});

test("opens the detail panel from a treemap cell click", async ({ page }) => {
  await page.goto("/coupling");
  await page.getByRole("button", { name: /SampleErp.Infrastructure: Highly coupled/ }).click();

  await expect(page.getByText("Coupling details")).toBeVisible();
  await expect(page.getByRole("link", { name: "View in Dependency Graph" })).toHaveAttribute(
    "href",
    "/graph/proj-infrastructure",
  );
});

test("switches to table view", async ({ page }) => {
  await page.goto("/coupling");
  await page.getByRole("button", { name: "Table", exact: true }).click();

  await expect(page.getByRole("columnheader", { name: "Instability" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "SampleErp.Domain" })).toBeVisible();
});
