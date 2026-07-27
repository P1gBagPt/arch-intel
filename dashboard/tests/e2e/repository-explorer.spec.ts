import { expect, test } from "@playwright/test";
import { API_ORIGIN, mockApi } from "./fixtures/api";

test.beforeEach(async ({ page }) => {
  await mockApi(page, API_ORIGIN);
});

test("groups fixture projects by layer", async ({ page }) => {
  await page.goto("/explorer");

  await expect(page.getByRole("button", { name: /Domain/ })).toBeVisible();
  await expect(page.getByRole("button", { name: /Application/ })).toBeVisible();
  await expect(page.getByRole("button", { name: /Infrastructure/ })).toBeVisible();
  await expect(page.getByText("SampleErp.Domain", { exact: true })).toBeVisible();
});

test("filters the tree via the search box", async ({ page }) => {
  await page.goto("/explorer");
  await page.getByPlaceholder("Filter projects…").fill("Infrastructure");

  await expect(page.getByText("SampleErp.Infrastructure", { exact: true })).toBeVisible();
  await expect(page.getByText("SampleErp.Domain", { exact: true })).not.toBeVisible();
});

test("shows an empty state for a query with no matches", async ({ page }) => {
  await page.goto("/explorer");
  await page.getByPlaceholder("Filter projects…").fill("NoSuchProject");
  await expect(page.getByText(/No projects match/)).toBeVisible();
});

test("deep-links from a project into the Dependency Graph", async ({ page }) => {
  await page.goto("/explorer");
  await page.getByRole("link", { name: /SampleErp.Infrastructure/ }).click();
  await expect(page).toHaveURL("/graph/proj-infrastructure");
  await expect(page.getByText("5 nodes · 3 edges")).toBeVisible();
});
