import { expect, test } from "@playwright/test";
import { API_ORIGIN, mockApi } from "./fixtures/api";

test.beforeEach(async ({ page }) => {
  await mockApi(page, API_ORIGIN);
});

test("lists services and navigates into detail", async ({ page }) => {
  await page.goto("/services");
  await expect(page.getByRole("link", { name: "CreateOrderCommandHandler" })).toBeVisible();

  await page.getByRole("link", { name: "CreateOrderCommandHandler" }).click();
  await expect(page).toHaveURL("/services/svc-handler");
  await expect(page.getByRole("heading", { name: "CreateOrderCommandHandler" })).toBeVisible();
});

test("tabs show fixture-correct counts and content", async ({ page }) => {
  await page.goto("/services/svc-handler");

  await expect(page.getByRole("button", { name: "Dependencies (1)" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Callers (0)" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Implements (0)" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Tests (1)" })).toBeVisible();

  // Dependencies is the default active tab.
  await expect(page.getByRole("link", { name: "IOrderRepository" })).toBeVisible();

  await page.getByRole("button", { name: "Tests (1)" }).click();
  await expect(page.getByRole("link", { name: "CreateOrderCommandHandlerTests" })).toBeVisible();
});

test("renders the mini dependency graph and navigates on click", async ({ page }) => {
  await page.goto("/services/svc-handler");
  await expect(page.getByText("No direct callers or dependencies")).not.toBeVisible();

  // React Flow tags each node wrapper with a stable `rf__node-{id}` test id — targets the mini
  // graph's node specifically, not the identically-labeled Dependencies tab link.
  await page.getByTestId("rf__node-iorder-repository").click();
  await expect(page).toHaveURL("/services/iorder-repository");
  await expect(page.getByRole("heading", { name: "IOrderRepository" })).toBeVisible();
});

test("shows an empty state when a service has no relations", async ({ page }) => {
  // order-repository's own fixture detail has zero dependencies and one caller — exercise the
  // "no dependencies OR callers" branch by pointing at a service with neither wired in the mock.
  await page.route(`${API_ORIGIN}/api/v1/repos/*/services/order-repository`, (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        data: {
          id: "order-repository",
          name: "OrderRepository",
          kind: "Repository",
          projectId: "proj-infrastructure",
          dependencies: [],
          callers: [],
          implements: [],
          tests: [],
        },
        page: null,
      }),
    }),
  );

  await page.goto("/services/order-repository");
  await expect(page.getByText("No direct callers or dependencies to visualize.")).toBeVisible();
});
