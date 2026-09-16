import { test, expect } from "@playwright/test";
import { injectAuth, loginApi } from "./helpers";

/**
 * E2E tests for the full Experimento workflow.
 * Each test authenticates via the API and injects the token
 * before navigating to a protected route.
 */
test.describe("Authenticated workflow", () => {
  let token: string;
  let cookies: Awaited<ReturnType<typeof loginApi>>["cookies"];

  test.beforeAll(async ({ request }) => {
    const auth = await loginApi(request);
    token = auth.token;
    cookies = auth.cookies;
  });

  test.beforeEach(async ({ page }) => {
    await injectAuth(page, cookies);
  });

  test("dashboard shows stat cards", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    const labels = ["Formulations", "Predictions", "Simulations", "Audit Integrity"];
    for (const label of labels) {
      await expect(page.getByRole("heading", { name: label })).toBeVisible();
    }
  });

  test("sidebar navigation links are present", async ({ page }) => {
    await page.goto("/");
    const links = ["Dashboard", "Formulations", "Predictions", "Simulations", "Knowledge Base", "Audit Trail"];
    for (const link of links) {
      await expect(page.getByRole("link", { name: link })).toBeVisible();
    }
  });

  test("formulations page: create project → formulation → version", async ({ page }) => {
    await page.goto("/formulations");
    await expect(page.getByRole("heading", { name: "Formulations" })).toBeVisible();

    // --- Create a project ---
    await page.getByRole("button", { name: "New project" }).click();
    await page.getByPlaceholder("Project name").fill(`E2E Project ${Date.now()}`);
    await page.getByRole("button", { name: "Create" }).click();
    // The project should now be selected in the dropdown
    const select = page.locator("select").first();
    await expect(select).toHaveValue(/^.+$/, { timeout: 5000 });

    // --- Create a formulation ---
    await page.getByRole("button", { name: "New formulation" }).click();
    const formName = `E2E Form ${Date.now()}`;
    await page.getByPlaceholder("Name").fill(formName);
    await page.getByPlaceholder("Target purpose").fill("Test solubility");
    await page.getByRole("button", { name: "Create" }).click();
    await expect(page.getByText(formName)).toBeVisible();

    // --- Expand the formulation and create a version ---
    await page.getByRole("button", { name: "New version" }).click();

    // Компонент выбирается строго из каталога PubChem: вводим запрос, выбираем "aspirin".
    const chemicalInput = page.getByPlaceholder("Search chemical…");
    await chemicalInput.fill("asp");
    await page.getByRole("button", { name: /^aspirin$/ }).click({ timeout: 20000 });
    // После резолва появляется плашка с CID и молекулярной массой.
    await expect(page.getByText(/CID 2244/)).toBeVisible({ timeout: 20000 });

    await page.getByPlaceholder("Proportion").fill("1.0");
    await page.getByPlaceholder("Role (optional)").fill("Active");
    await page.getByRole("button", { name: "Create version" }).click();
    // Version card should appear
    await expect(page.getByText("v1")).toBeVisible({ timeout: 15000 });
  });

  test("predictions page: cascading dropdowns and run prediction", async ({ page, request }) => {
    // Create a project + formulation + version via API so the test is deterministic
    const authHeader = { Authorization: `Bearer ${token}` };
    const project = await (await request.post("http://localhost:5126/api/projects", {
      data: { name: `PredTest ${Date.now()}`, description: "for prediction test" },
      headers: authHeader,
    })).json();
    const formulation = await (await request.post("http://localhost:5126/api/formulations", {
      data: { projectId: project.id, name: "Pred Formulation", targetPurpose: "test" },
      headers: authHeader,
    })).json();
    // Резолвим вещество в каталоге PubChem, чтобы получить CID для компонента.
    const aspirin = await (await request.get(
      "http://localhost:5126/api/chemicals/resolve?name=aspirin",
      { headers: authHeader },
    )).json();
    const versionResp = await request.post(`http://localhost:5126/api/formulations/${formulation.id}/versions`, {
      data: {
        components: [{
          pubChemCid: aspirin.pubChemCid,
          chemicalName: aspirin.name,
          casNumber: aspirin.casNumber,
          formula: aspirin.formula,
          molarMass: aspirin.molarMass,
          proportion: 1.0,
          role: "Active",
        }],
        conditions: { temperatureCelsius: 25, phTarget: 7, solvent: "water" },
      },
      headers: authHeader,
    });
    expect(versionResp.ok()).toBeTruthy();

    await page.goto("/predictions");
    await expect(page.getByRole("heading", { name: "Property Predictions" })).toBeVisible();

    const selects = page.locator("select");
    await expect(selects).toHaveCount(3);

    // Select the project we just created by label
    await selects.nth(0).selectOption({ label: project.name });
    await selects.nth(1).selectOption({ label: "Pred Formulation" });
    // Wait for the version to load
    await expect(selects.nth(2).locator("option")).toHaveCount(2, { timeout: 10000 });
    await selects.nth(2).selectOption({ index: 1 });

    // Run prediction
    await page.getByRole("button", { name: "Predict" }).click();
    await expect(page.getByText("Prediction result")).toBeVisible({ timeout: 30000 });
    await expect(page.getByText(/Success/)).toBeVisible();
  });

  test("audit trail page lists entries and supports integrity check", async ({ page }) => {
    await page.goto("/audit");
    await expect(page.getByRole("heading", { name: "Audit Trail" })).toBeVisible();
    await expect(page.locator("tbody tr").first()).toBeVisible({ timeout: 15000 });
    await page.getByRole("button", { name: "Verify integrity" }).click();
    await expect(page.getByText("Audit chain is intact.")).toBeVisible({ timeout: 15000 });
  });
});
