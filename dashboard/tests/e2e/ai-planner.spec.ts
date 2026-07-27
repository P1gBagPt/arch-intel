import { expect, test } from "@playwright/test";
import { API_ORIGIN, mockApi } from "./fixtures/api";

test.beforeEach(async ({ page }) => {
  await mockApi(page, API_ORIGIN);
});

test("submits an implementation plan prompt and renders the completed job result", async ({ page }) => {
  await page.goto("/planner");

  await page.getByPlaceholder("Describe the change you want to implement…").fill("Add a caching layer");
  await page.getByRole("button", { name: "Submit" }).click();

  await expect(page.getByText(/^Job job_fixture_plan$/)).toBeVisible();
  await expect(page.getByText("Risk: Unknown")).toBeVisible();
  await expect(page.getByText(/placeholder Planning Service/)).toBeVisible();
  await expect(page.getByText("CreateOrderCommandHandler", { exact: true })).toBeVisible();
  await expect(page.getByText("CreateOrderCommandHandlerTests", { exact: true })).toBeVisible();
});

test("submits an architecture analysis question and renders the completed job result", async ({ page }) => {
  await page.goto("/planner");

  await page.getByRole("button", { name: "Architecture Analysis" }).click();
  await page.getByPlaceholder("Ask a question about the architecture…").fill("What breaks if IOrderRepository changes?");
  await page.getByPlaceholder(/A single node ID to analyze/).fill("iorder-repository");
  await page.getByRole("button", { name: "Submit" }).click();

  await expect(page.getByText(/^Job job_fixture_analysis$/)).toBeVisible();
  await expect(page.getByText(/Removing or changing 'IOrderRepository'/)).toBeVisible();
  await expect(page.getByText("order-repository", { exact: true })).toBeVisible();
});

test("keeps prompt history entries independent and lets the user switch between them", async ({ page }) => {
  await page.goto("/planner");

  await page.getByPlaceholder("Describe the change you want to implement…").fill("Add a caching layer");
  await page.getByRole("button", { name: "Submit" }).click();
  await expect(page.getByText(/^Job job_fixture_plan$/)).toBeVisible();

  await page.getByRole("button", { name: "Architecture Analysis" }).click();
  await page.getByPlaceholder("Ask a question about the architecture…").fill("What breaks if IOrderRepository changes?");
  await page.getByPlaceholder(/A single node ID to analyze/).fill("iorder-repository");
  await page.getByRole("button", { name: "Submit" }).click();
  await expect(page.getByText(/^Job job_fixture_analysis$/)).toBeVisible();

  const history = page.getByTestId("planner-history").locator("li button");
  await expect(history).toHaveCount(2);
  // Most recent submission (the analysis) is prepended to the top of history.
  await expect(history.nth(0)).toContainText("What breaks if IOrderRepository changes?");
  await expect(history.nth(1)).toContainText("Add a caching layer");

  // Each entry's own text must stay intact — regression check for a bug where the prompt
  // textarea wasn't cleared between submissions, causing the next prompt to be typed into the
  // middle of the previous one's leftover text.
  await expect(history.nth(1)).not.toContainText("What breaks");

  await history.nth(1).click();
  await expect(page.getByText("Risk: Unknown")).toBeVisible();
});

test("shows a submit error surfaced from the API", async ({ page }) => {
  await page.route(`${API_ORIGIN}/api/v1/repos/*/implementation-plan`, (route) =>
    route.fulfill({
      status: 400,
      contentType: "application/json",
      body: JSON.stringify({ title: "Validation failed", status: 400, errors: { prompt: ["Prompt is required."] } }),
    }),
  );

  await page.goto("/planner");
  await page.getByPlaceholder("Describe the change you want to implement…").fill("x");
  await page.getByRole("button", { name: "Submit" }).click();

  await expect(page.getByText(/Failed to submit/)).toBeVisible();
});
