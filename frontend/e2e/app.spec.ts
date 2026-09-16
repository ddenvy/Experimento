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

  test("dashboard shows real stat cards", async ({ page }) => {
    await page.goto("/dashboard");
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    const labels = ["Projects", "Predictions run", "Lab outcomes", "Documents indexed"];
    for (const label of labels) {
      await expect(page.getByRole("heading", { name: label })).toBeVisible();
    }
  });

  test("sidebar navigation links are present", async ({ page }) => {
    await page.goto("/dashboard");
    const links = ["Dashboard", "Projects", "Knowledge Base", "Audit Trail"];
    for (const link of links) {
      await expect(page.getByRole("navigation").getByRole("link", { name: link, exact: true })).toBeVisible();
    }
  });

  test("legacy tool routes redirect to projects", async ({ page }) => {
    await page.goto("/formulations");
    await expect(page).toHaveURL(/\/projects$/);
    await page.goto("/predictions");
    await expect(page).toHaveURL(/\/projects$/);
    await page.goto("/simulations");
    await expect(page).toHaveURL(/\/projects$/);
  });

  test("golden path: project → formulation → version → prediction → simulation", async ({ page }) => {
    test.setTimeout(90_000);
    const marker = Date.now();
    const projectName = `E2E Project ${marker}`;
    const formName = `E2E Form ${marker}`;

    // --- Create a project on /projects ---
    await page.goto("/projects");
    await expect(page.getByRole("heading", { name: "Projects" })).toBeVisible();
    await page.getByRole("button", { name: "New project" }).first().click();
    await page.getByPlaceholder("Project name").fill(projectName);
    await page.getByRole("button", { name: "Create" }).click();
    await page
      .locator('a[href^="/projects/"]', { hasText: projectName })
      .click({ timeout: 5000 });

    // --- Project detail: create a formulation ---
    await expect(page).toHaveURL(/\/projects\/[0-9a-f-]+$/);
    await expect(page.getByRole("heading", { name: projectName })).toBeVisible();
    await page.getByRole("button", { name: "New formulation" }).first().click();
    await page.getByPlaceholder("Name").fill(formName);
    await page.getByPlaceholder("Target purpose").fill("Test solubility");
    await page.getByRole("button", { name: "Create" }).click();

    // --- Open formulation detail, create the first version on Composition tab ---
    await page
      .locator('a[href*="/formulations/"]', { hasText: formName })
      .click({ timeout: 5000 });
    await expect(page.getByRole("heading", { name: formName })).toBeVisible();
    await expect(page.getByRole("tab", { name: "Composition" })).toBeVisible();

    await page.getByRole("button", { name: "New version" }).click();

    // Компонент выбирается строго из каталога PubChem.
    const chemicalInput = page.getByPlaceholder("Search by name, formula or CAS…");
    await chemicalInput.fill("asp");
    await page.getByRole("option", { name: /aspirin/i }).first().click({ timeout: 20000 });
    await expect(page.getByText(/CID 2244/)).toBeVisible({ timeout: 20000 });

    await page.getByPlaceholder("Proportion").fill("1.0");
    await page.getByPlaceholder("Role (optional)").fill("Active");
    await page.getByRole("button", { name: "Create version" }).click();
    await expect(page.getByText("v1")).toBeVisible({ timeout: 15000 });

    // --- Predictions tab: version is already in context (no triple selector) ---
    await page.getByRole("tab", { name: "Predictions" }).click();
    const versionSelect = page.getByLabel("Select version");
    await expect(versionSelect.locator("option")).toHaveCount(1);
    const selectedVersionId = await versionSelect.inputValue();
    await expect(page.getByRole("heading", { name: "Run history" })).toBeVisible();

    await page.getByRole("button", { name: "Predict" }).click();
    await expect(page.getByRole("heading", { name: "Prediction result" })).toBeVisible({
      timeout: 45000,
    });
    // Завершённый прогон появляется в истории.
    await expect(page.getByText("Completed", { exact: true }).first()).toBeVisible();

    // --- Flow button leads to the Simulations tab with the same version ---
    await page.getByRole("button", { name: /Continue: run simulation/ }).click();
    await expect(page).toHaveURL(/tab=simulations/);
    await expect(page.getByRole("heading", { name: "Run simulation" })).toBeVisible();
    await expect(page.getByLabel("Select version")).toHaveValue(selectedVersionId);
  });

  test("command palette navigates to a project", async ({ page, request }) => {
    const authHeader = { Authorization: `Bearer ${token}` };
    const project = await (
      await request.post("http://localhost:5126/api/projects", {
        data: { name: `Palette ${Date.now()}`, description: "palette test" },
        headers: authHeader,
      })
    ).json();

    await page.goto("/dashboard");
    // Открываем палитру её кнопкой (клавиатурный ярлык — то же действие).
    await page.getByTestId("cmdk-trigger").click();
    const paletteInput = page.getByPlaceholder("Search pages, projects, formulations…");
    await expect(paletteInput).toBeVisible();
    await paletteInput.fill(project.name);
    await expect(page.getByRole("button", { name: new RegExp(project.name) })).toBeVisible({
      timeout: 10000,
    });
    await paletteInput.press("Enter");
    await expect(page).toHaveURL(new RegExp(`/projects/${project.id}$`));
  });

  test("mobile drawer exposes navigation", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/dashboard");
    const navProjects = page
      .getByRole("navigation")
      .getByRole("link", { name: "Projects", exact: true });
    await expect(navProjects).toHaveCount(0);

    await page.getByRole("button", { name: "Open navigation menu" }).click();
    await expect(navProjects).toBeVisible();
    await navProjects.click();
    await expect(page).toHaveURL(/\/projects$/);
  });

  test("prediction history keeps completed runs viewable", async ({ page, request }) => {
    test.setTimeout(90_000);
    const authHeader = { Authorization: `Bearer ${token}` };
    const project = await (
      await request.post("http://localhost:5126/api/projects", {
        data: { name: `HistTest ${Date.now()}`, description: "history test" },
        headers: authHeader,
      })
    ).json();
    const formulation = await (
      await request.post("http://localhost:5126/api/formulations", {
        data: { projectId: project.id, name: "Hist Formulation", targetPurpose: "test" },
        headers: authHeader,
      })
    ).json();
    const aspirin = await (
      await request.get("http://localhost:5126/api/chemicals/resolve?name=aspirin", {
        headers: authHeader,
      })
    ).json();
    const versionResp = await request.post(
      `http://localhost:5126/api/formulations/${formulation.id}/versions`,
      {
        data: {
          components: [
            {
              pubChemCid: aspirin.pubChemCid,
              chemicalName: aspirin.name,
              casNumber: aspirin.casNumber,
              formula: aspirin.formula,
              molarMass: aspirin.molarMass,
              proportion: 1.0,
              role: "Active",
            },
          ],
          conditions: { temperatureCelsius: 25, phTarget: 7, solvent: "water" },
        },
        headers: authHeader,
      }
    );
    expect(versionResp.ok()).toBeTruthy();

    await page.goto(
      `/projects/${project.id}/formulations/${formulation.id}?tab=predictions`
    );
    await page.getByRole("button", { name: "Predict" }).click();
    await expect(page.getByRole("heading", { name: "Prediction result" })).toBeVisible({
      timeout: 45000,
    });

    // Повторный заход на страницу показывает сохранённую историю без нового прогона.
    await page.reload();
    await expect(page.getByTestId("prediction-history")).toContainText("Completed");
    await page.getByText("Completed", { exact: true }).first().click();
    await expect(page.getByRole("heading", { name: "Prediction result" })).toBeVisible();
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
    await expect(page.getByText(docTitle)).toBeVisible({ timeout: 10000 });
    await expect(page.locator("li").filter({ hasText: docTitle }).getByText("Ready", { exact: true })).toBeVisible({ timeout: 30000 });

    // Семантический поиск находит содержимое документа по смыслу, а не по словам.
    await page.getByPlaceholder("Search literature, tables and internal notes…").fill("what compound makes coffee keep you awake");
    await page.getByRole("button", { name: "Search", exact: true }).click();
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

    // Уникальное имя файла: документы глобальны и видны между прогонами.
    const fileName = `${marker}.csv`;
    await page.locator('input[type="file"]').setInputFiles({
      name: fileName,
      mimeType: "text/csv",
      buffer: Buffer.from(csv, "utf-8"),
    });
    await expect(page.getByText(fileName, { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Index document" }).click();

    // Заголовок документа — имя файла без расширения; ждём готовности индексации.
    await expect(
      page.locator("li").filter({ hasText: `Paper · ${fileName}` }).getByText("Ready", { exact: true })
    ).toBeVisible({ timeout: 30000 });

    // Поиск "по смыслу" находит разобранную строку таблицы (пары заголовок: значение).
    // Документы глобальны и накапливаются между прогонами: идентичные строки из прошлых
    // загрузок имеют ту же близость, поэтому проверяем паттерн любой E2E-загрузки.
    await page.getByPlaceholder("Search literature, tables and internal notes…").fill("what medicine helps bring down a fever");
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Results" })).toBeVisible({ timeout: 20000 });
    await expect(page.getByText(/compound: E2ETable\d+ paracetamol/).first()).toBeVisible();
  });
});
