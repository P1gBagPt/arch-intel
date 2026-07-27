import { expect, test } from "@playwright/test";
import { API_ORIGIN, mockApi } from "./fixtures/api";
import { clickGraphNode } from "./helpers";

test.beforeEach(async ({ page }) => {
  await mockApi(page, API_ORIGIN);
});

test("loads the fixture graph and shows the node/edge count", async ({ page }) => {
  await page.goto("/graph");
  await expect(page.getByText("5 nodes · 3 edges")).toBeVisible();
});

test("clicking a node opens the detail drawer with correct data and links", async ({ page }) => {
  await page.goto("/graph");
  await clickGraphNode(page, "order-repository");

  await expect(page.getByText("Node details")).toBeVisible();
  await expect(page.getByText("OrderRepository", { exact: true })).toBeVisible();
  await expect(page.getByText("Repository", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Open in Service Explorer" })).toHaveAttribute(
    "href",
    "/services/order-repository",
  );
  await expect(page.getByRole("link", { name: "Run Impact Analysis" })).toHaveAttribute(
    "href",
    "/impact/order-repository",
  );
});

test("closing the drawer clears the selection", async ({ page }) => {
  await page.goto("/graph");
  await clickGraphNode(page, "order-repository");
  await expect(page.getByText("Node details")).toBeVisible();

  await page.getByRole("button", { name: "Close" }).click();
  await expect(page.getByText("Node details")).not.toBeVisible();
});

test("exports the graph as Mermaid", async ({ page }) => {
  await page.goto("/graph");
  await page.getByRole("button", { name: "Export as Mermaid" }).click();

  await expect(page.getByRole("heading", { name: "Mermaid export" })).toBeVisible();
  await expect(page.getByText("graph TD", { exact: false })).toBeVisible();
  await expect(page.getByText("IOrderRepository", { exact: false }).first()).toBeVisible();
});
