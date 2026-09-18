import { test, expect } from "@playwright/test";
import { createHash } from "crypto";
import { readFileSync } from "fs";
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
    const links = ["Dashboard", "Projects", "Knowledge Base", "Model Scorecard", "Audit Trail"];
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

  test("lab journal: review and lab outcome persist and feed the scorecard", async ({ page, request }) => {
    test.setTimeout(90_000);
    const authHeader = { Authorization: `Bearer ${token}` };
    const marker = Date.now();
    const project = await (
      await request.post("http://localhost:5126/api/projects", {
        data: { name: `Outcome ${marker}`, description: "outcome e2e" },
        headers: authHeader,
      })
    ).json();
    const formulation = await (
      await request.post("http://localhost:5126/api/formulations", {
        data: { projectId: project.id, name: `Outcome Form ${marker}`, targetPurpose: "test" },
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

    // --- Review: комментарий и отправка ---
    await page.getByLabel("Review comment").fill("E2E approved");
    await page.getByTestId("submit-review").click();
    await expect(page.getByTestId("review-list")).toContainText("Approved", { timeout: 10000 });

    // --- Lab outcome: фактический успех + наблюдаемая токсичность + заметки ---
    await page.getByLabel("Observed toxicity").fill("0.2");
    await page.getByLabel("Lab notes").fill("E2E lab outcome");
    await page.getByTestId("save-outcome").click();
    // После первой сохранённой записи кнопка становится "Update outcome"...
    await expect(page.getByRole("button", { name: "Update outcome" })).toBeVisible({ timeout: 10000 });
    // ...а в истории у прогона появляется отметка о записанном исходе.
    await expect(page.getByLabel("Lab outcome recorded").first()).toBeVisible();

    // Перезаход: открываем прогон из истории — данные сохранились.
    await page.reload();
    await page.getByText("Completed", { exact: true }).first().click();
    await expect(page.getByRole("heading", { name: "Prediction result" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Update outcome" })).toBeVisible();
    await expect(page.getByLabel("Lab notes")).toHaveValue("E2E lab outcome");

    // Scorecard видит исход, а страница /models рендерит таблицу.
    const scorecardResp = await request.get("http://localhost:5126/api/models/scorecard", {
      headers: authHeader,
    });
    const scorecard = await scorecardResp.json();
    expect(scorecard.some((m: { withOutcome: number }) => m.withOutcome >= 1)).toBeTruthy();

    await page.goto("/models");
    await expect(page.getByRole("heading", { name: "Model Scorecard" })).toBeVisible();
    await expect(page.getByTestId("model-scorecard-table")).toBeVisible();
  });

  test("model scorecard is reachable from the sidebar", async ({ page }) => {
    await page.goto("/dashboard");
    await page
      .getByRole("navigation")
      .getByRole("link", { name: "Model Scorecard", exact: true })
      .click();
    await expect(page).toHaveURL(/\/models$/);
    await expect(page.getByRole("heading", { name: "Model Scorecard" })).toBeVisible();
  });

  test("compare versions highlights modified and removed components", async ({ page, request }) => {
    const authHeader = { Authorization: `Bearer ${token}` };
    const marker = Date.now();
    const project = await (
      await request.post("http://localhost:5126/api/projects", {
        data: { name: `Compare ${marker}`, description: "compare e2e" },
        headers: authHeader,
      })
    ).json();
    const formulation = await (
      await request.post("http://localhost:5126/api/formulations", {
        data: { projectId: project.id, name: `Compare Form ${marker}`, targetPurpose: "test" },
        headers: authHeader,
      })
    ).json();

    const aspirin = await (
      await request.get("http://localhost:5126/api/chemicals/resolve?name=aspirin", {
        headers: authHeader,
      })
    ).json();
    const sodiumChloride = await (
      await request.get("http://localhost:5126/api/chemicals/resolve?name=sodium%20chloride", {
        headers: authHeader,
      })
    ).json();

    const component = (
      cid: number,
      name: string,
      cas: string | null,
      formula: string | null,
      molarMass: number,
      proportion: number,
      role: string
    ) => ({ pubChemCid: cid, chemicalName: name, casNumber: cas, formula, molarMass, proportion, role });

    const conditions = { temperatureCelsius: 25, phTarget: 7, solvent: "water" };

    const v1Resp = await request.post(
      `http://localhost:5126/api/formulations/${formulation.id}/versions`,
      {
        data: {
          components: [
            component(aspirin.pubChemCid, aspirin.name, aspirin.casNumber, aspirin.formula, aspirin.molarMass, 0.6, "Active"),
            component(sodiumChloride.pubChemCid, sodiumChloride.name, sodiumChloride.casNumber, sodiumChloride.formula, sodiumChloride.molarMass, 0.4, "Excipient"),
          ],
          conditions,
        },
        headers: authHeader,
      }
    );
    expect(v1Resp.ok()).toBeTruthy();

    const v2Resp = await request.post(
      `http://localhost:5126/api/formulations/${formulation.id}/versions`,
      {
        data: {
          // Аспирин изменён (0.6 → 1.0), хлорид удалён.
          components: [
            component(aspirin.pubChemCid, aspirin.name, aspirin.casNumber, aspirin.formula, aspirin.molarMass, 1.0, "Active"),
          ],
          conditions,
        },
        headers: authHeader,
      }
    );
    expect(v2Resp.ok()).toBeTruthy();

    await page.goto(
      `/projects/${project.id}/formulations/${formulation.id}?tab=composition`
    );
    await expect(page.getByRole("heading", { name: "Compare versions" })).toBeVisible();
    await page.getByTestId("compare-versions").click();

    const result = page.getByTestId("comparison-result");
    await expect(result).toBeVisible({ timeout: 10000 });

    const removedRow = result.locator("tr[data-change='Removed']", { hasText: "Sodium chloride" });
    await expect(removedRow).toBeVisible();
    await expect(removedRow).toContainText("40.0%");

    const modifiedRow = result.locator("tr[data-change='Modified']", { hasText: "Aspirin" });
    await expect(modifiedRow).toBeVisible();
    await expect(modifiedRow).toContainText("60.0%");
    await expect(modifiedRow).toContainText("100.0%");
    await expect(modifiedRow).toContainText("40.0 pp");
  });

  test("version report renders markdown and downloads as .md", async ({ page, request }) => {
    const authHeader = { Authorization: `Bearer ${token}` };
    const marker = Date.now();
    const project = await (
      await request.post("http://localhost:5126/api/projects", {
        data: { name: `Report ${marker}`, description: "report e2e" },
        headers: authHeader,
      })
    ).json();
    const formulation = await (
      await request.post("http://localhost:5126/api/formulations", {
        data: { projectId: project.id, name: `Report Form ${marker}`, targetPurpose: "report test" },
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
      `/projects/${project.id}/formulations/${formulation.id}?tab=report`
    );
    await expect(page.getByRole("heading", { name: "Version report" })).toBeVisible();

    // Кнопки экспорта появляются только после генерации.
    await expect(page.getByTestId("download-report")).toHaveCount(0);
    await page.getByTestId("generate-report").click();

    const report = page.getByTestId("report-content");
    await expect(report).toBeVisible({ timeout: 10000 });
    await expect(report).toContainText("Formulation Report:");
    await expect(report).toContainText("Composition");
    await expect(report).toContainText("Aspirin");

    // Print/PDF доступна (сам диалог печати в e2e не вызываем).
    await expect(page.getByTestId("print-report")).toBeEnabled();

    // Download .md: браузер выдаёт событие загрузки с корректным именем файла.
    const downloadPromise = page.waitForEvent("download");
    await page.getByTestId("download-report").click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/\.md$/);

    // Таблицы отчёта не растягивают страницу по горизонтали.
    const overflow = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    }));
    expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.innerWidth);
  });

  test("audit trail page lists entries and supports integrity check", async ({ page }) => {
    await page.goto("/audit");
    await expect(page.getByRole("heading", { name: "Audit Trail" })).toBeVisible();
    await expect(page.locator("tbody tr").first()).toBeVisible({ timeout: 15000 });
    await page.getByRole("button", { name: "Verify integrity" }).click();
    await expect(page.getByText("Audit chain is intact.")).toBeVisible({ timeout: 15000 });
  });

  test("audit export downloads a self-verifying ALCOA+ package", async ({ page }) => {
    await page.goto("/audit");
    await expect(page.getByRole("heading", { name: "Audit Trail" })).toBeVisible();

    const downloadPromise = page.waitForEvent("download");
    await page.getByTestId("export-audit").click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/^audit-export-.*\.md$/);

    const stream = await download.createReadStream();
    const chunks: Buffer[] = [];
    for await (const chunk of stream) chunks.push(chunk as Buffer);
    const markdown = Buffer.concat(chunks).toString("utf-8");

    expect(markdown).toContain("# Audit Export Package");
    expect(markdown).toContain("## ALCOA+ Compliance Assessment");
    expect(markdown).toContain("## Formulation Versions Inventory");
    expect(markdown).toContain("## Full Audit Trail");
    expect(markdown).toContain("| Attributable |");
    expect(markdown).toContain("| Accurate |");

    // Отпечаток документа обязан воспроизводиться по его же телу.
    const marker = "**Document fingerprint (SHA-256):**";
    const markerIndex = markdown.indexOf(marker);
    expect(markerIndex).toBeGreaterThan(0);
    const expected = createHash("sha256")
      .update(Buffer.from(markdown.slice(0, markerIndex), "utf-8"))
      .digest("hex");
    const actual = markdown.slice(markerIndex + marker.length).match(/[0-9a-f]{64}/)?.[0];
    expect(actual).toBe(expected);
  });

  test("composition tab suggests substitute components from the catalog", async ({ page, request }) => {
    const authHeader = { Authorization: `Bearer ${token}` };
    const marker = Date.now();
    const project = await (
      await request.post("http://localhost:5126/api/projects", {
        data: { name: `Substitute ${marker}`, description: "substitute e2e" },
        headers: authHeader,
      })
    ).json();
    const formulation = await (
      await request.post("http://localhost:5126/api/formulations", {
        data: { projectId: project.id, name: `Substitute Form ${marker}`, targetPurpose: "test" },
        headers: authHeader,
      })
    ).json();

    const aspirin = await (
      await request.get("http://localhost:5126/api/chemicals/resolve?name=aspirin", {
        headers: authHeader,
      })
    ).json();
    // Гарантируем близкий аналог в каталоге: салициловая кислота попадает
    // в окно массы аспирина, поэтому кандидат найдётся даже на чистой базе.
    await request.get("http://localhost:5126/api/chemicals/resolve?name=salicylic%20acid", {
      headers: authHeader,
    });

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
      `/projects/${project.id}/formulations/${formulation.id}?tab=composition`
    );
    await page.getByRole("button", { name: "Find substitute" }).first().click();

    const panel = page.getByTestId("substitute-panel");
    await expect(panel).toBeVisible();
    await expect(panel).toContainText("Substitutes for");

    // Кандидаты приходят ранжированными и с расшифровкой совпадения.
    await expect(page.getByTestId("substitute-list")).toBeVisible({ timeout: 15000 });
    await expect(panel).toContainText("Salicylic");
    await expect(panel).toContainText("% match");
  });

  test("scale-up tab assesses a version for a pilot batch size", async ({ page, request }) => {
    const authHeader = { Authorization: `Bearer ${token}` };
    const marker = Date.now();
    const project = await (
      await request.post("http://localhost:5126/api/projects", {
        data: { name: `Scale-up ${marker}`, description: "scale-up e2e" },
        headers: authHeader,
      })
    ).json();
    const formulation = await (
      await request.post("http://localhost:5126/api/formulations", {
        data: { projectId: project.id, name: `Scale-up Form ${marker}`, targetPurpose: "test" },
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
          conditions: { temperatureCelsius: 25, pressureKPa: 101.3, phTarget: 7, solvent: "water" },
        },
        headers: authHeader,
      }
    );
    expect(versionResp.ok()).toBeTruthy();

    await page.goto(`/projects/${project.id}/formulations/${formulation.id}?tab=scaleup`);
    await page.getByTestId("assess-scale-up").click();

    const result = page.getByTestId("scale-up-result");
    await expect(result).toBeVisible();
    // 10 л — уже пилотный масштаб: инертная рецептура теряет теплоотвод, но переносима.
    await expect(page.getByTestId("scale-up-score")).toHaveText("92");
    await expect(result).toContainText("Scalable with controls");
    await expect(page.getByTestId("scale-up-findings")).toContainText("Thermal");
    await expect(result).toContainText("cooling capacity per litre");
  });

  test("next experiment tab turns a simulation run into a ranked plan", async ({ page, request }) => {
    test.setTimeout(90_000);
    const authHeader = { Authorization: `Bearer ${token}` };
    const marker = Date.now();
    const project = await (
      await request.post("http://localhost:5126/api/projects", {
        data: { name: `Next ${marker}`, description: "next experiment e2e" },
        headers: authHeader,
      })
    ).json();
    const formulation = await (
      await request.post("http://localhost:5126/api/formulations", {
        data: { projectId: project.id, name: `Next Form ${marker}`, targetPurpose: "test" },
        headers: authHeader,
      })
    ).json();
    const aspirin = await (
      await request.get("http://localhost:5126/api/chemicals/resolve?name=aspirin", {
        headers: authHeader,
      })
    ).json();

    const version = await (
      await request.post(`http://localhost:5126/api/formulations/${formulation.id}/versions`, {
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
      })
    ).json();

    // Прогоняем симуляцию через API и дожидаемся результата: он и есть источник рекомендаций.
    const job = await (
      await request.post(
        `http://localhost:5126/api/simulations/formulation-versions/${version.id}/simulations`,
        {
          data: {
            iterations: 10,
            varyConcentrations: true,
            varyTemperature: true,
            varyPh: false,
            seed: 7,
            targetMetric: "success",
          },
          headers: authHeader,
        }
      )
    ).json();
    const jobUrl = `http://localhost:5126/api/simulations/simulation-jobs/${job.id}`;
    let status = "";
    for (let i = 0; i < 40 && status !== "Completed"; i++) {
      status = (await (await request.get(jobUrl, { headers: authHeader })).json()).status;
      if (status !== "Completed") await new Promise((resolve) => setTimeout(resolve, 1000));
    }
    expect(status).toBe("Completed");

    await page.goto(`/projects/${project.id}/formulations/${formulation.id}?tab=nextexperiment`);

    const item = page.getByTestId("next-experiment-item").first();
    await expect(item).toBeVisible({ timeout: 15000 });
    // Версия не проверена в лаборатории, поэтому лид симуляции — только гипотеза.
    await expect(item).toContainText("Simulation lead");
    await expect(item).toContainText("Low confidence");
    await expect(item).toContainText("Run v1 with");
    await expect(item).toContainText("no laboratory outcome recorded for this version");
    await expect(page.getByText("Lab outcomes:")).toBeVisible();
  });

  test("knowledge base: upload a laboratory notebook scan, OCR it and find it semantically", async ({ page }) => {
    await page.goto("/knowledge");
    await expect(page.getByRole("heading", { name: "Knowledge Base" })).toBeVisible();

    // Уникальное имя файла: документы глобальны и видны между прогонами.
    const fileName = `E2EScan${Date.now()}.png`;
    await page.locator('input[type="file"]').setInputFiles({
      name: fileName,
      mimeType: "image/png",
      buffer: readFileSync("e2e/fixtures/lab-note.png"),
    });
    await expect(page.getByText(fileName, { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Index document" }).click();

    // Распознавание текста моделью + индексация занимают заметно больше времени, чем для текстовых файлов.
    await expect(
      page.locator("li").filter({ hasText: `Paper · ${fileName}` }).getByText("Ready", { exact: true })
    ).toBeVisible({ timeout: 90_000 });

    // Распознанное содержимое попадает в общий семантический поиск.
    await page
      .getByPlaceholder("Search literature, tables and internal notes…")
      .fill("which solvent was used for the aspirin solubility experiment");
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Results" })).toBeVisible({ timeout: 20000 });
    await expect(page.getByText(/acetylsalicylic acid/).first()).toBeVisible();
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
