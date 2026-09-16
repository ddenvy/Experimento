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

  // Логинимся заново В КАЖДОМ тесте: Playwright выдаёт каждому тесту новый браузерный
  // контекст, а refresh-токен одноразовый (ротация + reuse detection). Один общий логин
  // привёл бы к предъявлению уже отозванного токена во втором тесте.
  test.beforeEach(async ({ request, page }) => {
    const auth = await loginApi(request);
    token = auth.token;
    cookies = auth.cookies;
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
    const chemicalInput = page.getByPlaceholder("Search by name, formula or CAS…");
    await chemicalInput.fill("asp");
    await page.getByRole("option", { name: /aspirin/i }).first().click({ timeout: 20000 });
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

  test("knowledge base: index a document and find it via semantic search", async ({ page }) => {
    await page.goto("/knowledge");
    await expect(page.getByRole("heading", { name: "Knowledge Base" })).toBeVisible();

    // Заполняем форму загрузки и отправляем на индексацию.
    const docTitle = `E2E KB Doc ${Date.now()}`;
    await page.getByPlaceholder("Document title").fill(docTitle);
    await page.getByPlaceholder("Reference (DOI, patent number, experiment ID — optional)").fill("E2E-REF-1");
    await page.getByPlaceholder(/Paste paper abstract/).fill(
      "Caffeine is a central nervous system stimulant found in coffee and tea. It increases alertness."
    );
    await page.getByRole("button", { name: "Index document" }).click();

    // Документ появляется в списке и доходит до Ready (поллинг на странице каждые 2 секунды).
    // В DOM статус "Ready" (визуально он uppercase через CSS), поэтому ищем в mixed-case.
    await expect(page.getByText(docTitle)).toBeVisible({ timeout: 10000 });
    await expect(page.locator("li").filter({ hasText: docTitle }).getByText("Ready", { exact: true })).toBeVisible({ timeout: 30000 });

    // Семантический поиск находит содержимое документа по смыслу, а не по словам.
    await page.getByPlaceholder("Search literature, tables and internal notes…").fill("what compound makes coffee keep you awake");
    await page.getByRole("button", { name: "Search" }).click();
    await expect(page.getByRole("heading", { name: "Results" })).toBeVisible({ timeout: 20000 });
    await expect(page.getByText(/Caffeine is a central nervous system stimulant/).first()).toBeVisible();
  });

  test("knowledge base: upload a CSV table, index rows and find them semantically", async ({ page }) => {
    await page.goto("/knowledge");
    await expect(page.getByRole("heading", { name: "Knowledge Base" })).toBeVisible();

    const marker = `E2ETable${Date.now()}`;
    const csv = [
      "compound,usage,solubility",
      `${marker} paracetamol,antipyretic medicine used to reduce fever and high temperature,14 mg/mL`,
      `${marker} ibuprofen,non-steroidal anti-inflammatory painkiller,0.02 mg/mL`,
      "",
    ].join("\n");

    // Скрытый input[type=file] принимает файл программно.
    await page.locator('input[type="file"]').setInputFiles({
      name: "lab-table.csv",
      mimeType: "text/csv",
      buffer: Buffer.from(csv, "utf-8"),
    });
    await expect(page.getByText("lab-table.csv")).toBeVisible();
    await page.getByRole("button", { name: "Index document" }).click();

    // Заголовок документа — имя файла без расширения; ждём готовности индексации.
    await expect(page.locator("li").filter({ hasText: "lab-table" }).getByText("Ready", { exact: true })).toBeVisible({ timeout: 30000 });

    // Поиск "по смыслу" находит разобранную строку таблицы (пары заголовок: значение).
    await page.getByPlaceholder("Search literature, tables and internal notes…").fill("what medicine helps bring down a fever");
    await page.getByRole("button", { name: "Search" }).click();
    await expect(page.getByRole("heading", { name: "Results" })).toBeVisible({ timeout: 20000 });
    await expect(page.getByText(new RegExp(`compound: ${marker} paracetamol`)).first()).toBeVisible();
  });
});
