import type { Page } from "@playwright/test";
import type { Core } from "cytoscape";

// The unscoped /graph route defaults to a Project-kind-only "project map" (DependencyGraphView) —
// tests that need the fixture's full small graph (node clicks, Mermaid export) must explicitly
// opt into "Load full graph", same as a real user would, including its confirm() dialog.
export async function loadFullGraph(page: Page) {
  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Load full graph" }).click();
}

// Next.js dev mode double-invokes effects (React StrictMode) — the Cytoscape renderer's mount
// effect briefly creates-then-destroys a first cy instance before the real one settles (see
// components/graph/renderers/CytoscapeGraphRenderer.tsx). Waiting for `__cyInstance` to exist
// isn't quite enough on its own since that can observe the soon-to-be-destroyed instance; give
// it a brief moment to settle before driving it.
export async function clickGraphNode(page: Page, nodeId: string) {
  await page.waitForFunction(() => !!(window as unknown as { __cyInstance?: unknown }).__cyInstance);
  await page.waitForTimeout(150);
  await page.evaluate((id) => {
    const cy = (window as unknown as { __cyInstance: Core }).__cyInstance;
    cy.getElementById(id).emit("tap");
  }, nodeId);
}
