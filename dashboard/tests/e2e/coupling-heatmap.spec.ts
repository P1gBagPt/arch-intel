import { expect, test } from "@playwright/test";
import { API_ORIGIN, mockApi } from "./fixtures/api";

test.beforeEach(async ({ page }) => {
  await mockApi(page, API_ORIGIN);
});

test("shows the coupling grid sorted by instability with band labels", async ({ page }) => {
  await page.goto("/coupling");

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

test("switches to table view", async ({ page }) => {
  await page.goto("/coupling");
  await page.getByRole("button", { name: "Table", exact: true }).click();

  await expect(page.getByRole("columnheader", { name: "Instability" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "SampleErp.Domain" })).toBeVisible();
});
