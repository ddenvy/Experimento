# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: app.spec.ts >> Authenticated workflow >> prediction history keeps completed runs viewable
- Location: e2e\app.spec.ts:150:7

# Error details

```
Error: expect(received).toBeTruthy()

Received: false
```

# Test source

```ts
  90  |     await page.getByRole("button", { name: "Create version" }).click();
  91  |     await expect(page.getByText("v1")).toBeVisible({ timeout: 15000 });
  92  | 
  93  |     // --- Predictions tab: version is already in context (no triple selector) ---
  94  |     await page.getByRole("tab", { name: "Predictions" }).click();
  95  |     const versionSelect = page.getByLabel("Select version");
  96  |     await expect(versionSelect.locator("option")).toHaveCount(1);
  97  |     const selectedVersionId = await versionSelect.inputValue();
  98  |     await expect(page.getByRole("heading", { name: "Run history" })).toBeVisible();
  99  | 
  100 |     await page.getByRole("button", { name: "Predict" }).click();
  101 |     await expect(page.getByRole("heading", { name: "Prediction result" })).toBeVisible({
  102 |       timeout: 45000,
  103 |     });
  104 |     // Завершённый прогон появляется в истории.
  105 |     await expect(page.getByText("Completed", { exact: true }).first()).toBeVisible();
  106 | 
  107 |     // --- Flow button leads to the Simulations tab with the same version ---
  108 |     await page.getByRole("button", { name: /Continue: run simulation/ }).click();
  109 |     await expect(page).toHaveURL(/tab=simulations/);
  110 |     await expect(page.getByRole("heading", { name: "Run simulation" })).toBeVisible();
  111 |     await expect(page.getByLabel("Select version")).toHaveValue(selectedVersionId);
  112 |   });
  113 | 
  114 |   test("command palette navigates to a project", async ({ page, request }) => {
  115 |     const authHeader = { Authorization: `Bearer ${token}` };
  116 |     const project = await (
  117 |       await request.post("http://localhost:5126/api/projects", {
  118 |         data: { name: `Palette ${Date.now()}`, description: "palette test" },
  119 |         headers: authHeader,
  120 |       })
  121 |     ).json();
  122 | 
  123 |     await page.goto("/dashboard");
  124 |     // Открываем палитру её кнопкой (клавиатурный ярлык — то же действие).
  125 |     await page.getByTestId("cmdk-trigger").click();
  126 |     const paletteInput = page.getByPlaceholder("Search pages, projects, formulations…");
  127 |     await expect(paletteInput).toBeVisible();
  128 |     await paletteInput.fill(project.name);
  129 |     await expect(page.getByRole("button", { name: new RegExp(project.name) })).toBeVisible({
  130 |       timeout: 10000,
  131 |     });
  132 |     await paletteInput.press("Enter");
  133 |     await expect(page).toHaveURL(new RegExp(`/projects/${project.id}$`));
  134 |   });
  135 | 
  136 |   test("mobile drawer exposes navigation", async ({ page }) => {
  137 |     await page.setViewportSize({ width: 390, height: 844 });
  138 |     await page.goto("/dashboard");
  139 |     const navProjects = page
  140 |       .getByRole("navigation")
  141 |       .getByRole("link", { name: "Projects", exact: true });
  142 |     await expect(navProjects).toHaveCount(0);
  143 | 
  144 |     await page.getByRole("button", { name: "Open navigation menu" }).click();
  145 |     await expect(navProjects).toBeVisible();
  146 |     await navProjects.click();
  147 |     await expect(page).toHaveURL(/\/projects$/);
  148 |   });
  149 | 
  150 |   test("prediction history keeps completed runs viewable", async ({ page, request }) => {
  151 |     test.setTimeout(90_000);
  152 |     const authHeader = { Authorization: `Bearer ${token}` };
  153 |     const project = await (
  154 |       await request.post("http://localhost:5126/api/projects", {
  155 |         data: { name: `HistTest ${Date.now()}`, description: "history test" },
  156 |         headers: authHeader,
  157 |       })
  158 |     ).json();
  159 |     const formulation = await (
  160 |       await request.post("http://localhost:5126/api/formulations", {
  161 |         data: { projectId: project.id, name: "Hist Formulation", targetPurpose: "test" },
  162 |         headers: authHeader,
  163 |       })
  164 |     ).json();
  165 |     const aspirin = await (
  166 |       await request.get("http://localhost:5126/api/chemicals/resolve?name=aspirin", {
  167 |         headers: authHeader,
  168 |       })
  169 |     ).json();
  170 |     const versionResp = await request.post(
  171 |       `http://localhost:5126/api/formulations/${formulation.id}/versions`,
  172 |       {
  173 |         data: {
  174 |           components: [
  175 |             {
  176 |               pubChemCid: aspirin.pubChemCid,
  177 |               chemicalName: aspirin.name,
  178 |               casNumber: aspirin.casNumber,
  179 |               formula: aspirin.formula,
  180 |               molarMass: aspirin.molarMass,
  181 |               proportion: 1.0,
  182 |               role: "Active",
  183 |             },
  184 |           ],
  185 |           conditions: { temperatureCelsius: 25, phTarget: 7, solvent: "water" },
  186 |         },
  187 |         headers: authHeader,
  188 |       }
  189 |     );
> 190 |     expect(versionResp.ok()).toBeTruthy();
      |                              ^ Error: expect(received).toBeTruthy()
  191 | 
  192 |     await page.goto(
  193 |       `/projects/${project.id}/formulations/${formulation.id}?tab=predictions`
  194 |     );
  195 |     await page.getByRole("button", { name: "Predict" }).click();
  196 |     await expect(page.getByRole("heading", { name: "Prediction result" })).toBeVisible({
  197 |       timeout: 45000,
  198 |     });
  199 | 
  200 |     // Повторный заход на страницу показывает сохранённую историю без нового прогона.
  201 |     await page.reload();
  202 |     await expect(page.getByTestId("prediction-history")).toContainText("Completed");
  203 |     await page.getByText("Completed", { exact: true }).first().click();
  204 |     await expect(page.getByRole("heading", { name: "Prediction result" })).toBeVisible();
  205 |   });
  206 | 
  207 |   test("lab journal: review and lab outcome persist and feed the scorecard", async ({ page, request }) => {
  208 |     test.setTimeout(90_000);
  209 |     const authHeader = { Authorization: `Bearer ${token}` };
  210 |     const marker = Date.now();
  211 |     const project = await (
  212 |       await request.post("http://localhost:5126/api/projects", {
  213 |         data: { name: `Outcome ${marker}`, description: "outcome e2e" },
  214 |         headers: authHeader,
  215 |       })
  216 |     ).json();
  217 |     const formulation = await (
  218 |       await request.post("http://localhost:5126/api/formulations", {
  219 |         data: { projectId: project.id, name: `Outcome Form ${marker}`, targetPurpose: "test" },
  220 |         headers: authHeader,
  221 |       })
  222 |     ).json();
  223 |     const aspirin = await (
  224 |       await request.get("http://localhost:5126/api/chemicals/resolve?name=aspirin", {
  225 |         headers: authHeader,
  226 |       })
  227 |     ).json();
  228 |     const versionResp = await request.post(
  229 |       `http://localhost:5126/api/formulations/${formulation.id}/versions`,
  230 |       {
  231 |         data: {
  232 |           components: [
  233 |             {
  234 |               pubChemCid: aspirin.pubChemCid,
  235 |               chemicalName: aspirin.name,
  236 |               casNumber: aspirin.casNumber,
  237 |               formula: aspirin.formula,
  238 |               molarMass: aspirin.molarMass,
  239 |               proportion: 1.0,
  240 |               role: "Active",
  241 |             },
  242 |           ],
  243 |           conditions: { temperatureCelsius: 25, phTarget: 7, solvent: "water" },
  244 |         },
  245 |         headers: authHeader,
  246 |       }
  247 |     );
  248 |     expect(versionResp.ok()).toBeTruthy();
  249 | 
  250 |     await page.goto(
  251 |       `/projects/${project.id}/formulations/${formulation.id}?tab=predictions`
  252 |     );
  253 |     await page.getByRole("button", { name: "Predict" }).click();
  254 |     await expect(page.getByRole("heading", { name: "Prediction result" })).toBeVisible({
  255 |       timeout: 45000,
  256 |     });
  257 | 
  258 |     // --- Review: комментарий и отправка ---
  259 |     await page.getByLabel("Review comment").fill("E2E approved");
  260 |     await page.getByTestId("submit-review").click();
  261 |     await expect(page.getByTestId("review-list")).toContainText("Approved", { timeout: 10000 });
  262 | 
  263 |     // --- Lab outcome: фактический успех + наблюдаемая токсичность + заметки ---
  264 |     await page.getByLabel("Observed toxicity").fill("0.2");
  265 |     await page.getByLabel("Lab notes").fill("E2E lab outcome");
  266 |     await page.getByTestId("save-outcome").click();
  267 |     // После первой сохранённой записи кнопка становится "Update outcome"...
  268 |     await expect(page.getByRole("button", { name: "Update outcome" })).toBeVisible({ timeout: 10000 });
  269 |     // ...а в истории у прогона появляется отметка о записанном исходе.
  270 |     await expect(page.getByLabel("Lab outcome recorded").first()).toBeVisible();
  271 | 
  272 |     // Перезаход: открываем прогон из истории — данные сохранились.
  273 |     await page.reload();
  274 |     await page.getByText("Completed", { exact: true }).first().click();
  275 |     await expect(page.getByRole("heading", { name: "Prediction result" })).toBeVisible();
  276 |     await expect(page.getByRole("button", { name: "Update outcome" })).toBeVisible();
  277 |     await expect(page.getByLabel("Lab notes")).toHaveValue("E2E lab outcome");
  278 | 
  279 |     // Scorecard видит исход, а страница /models рендерит таблицу.
  280 |     const scorecardResp = await request.get("http://localhost:5126/api/models/scorecard", {
  281 |       headers: authHeader,
  282 |     });
  283 |     const scorecard = await scorecardResp.json();
  284 |     expect(scorecard.some((m: { withOutcome: number }) => m.withOutcome >= 1)).toBeTruthy();
  285 | 
  286 |     await page.goto("/models");
  287 |     await expect(page.getByRole("heading", { name: "Model Scorecard" })).toBeVisible();
  288 |     await expect(page.getByTestId("model-scorecard-table")).toBeVisible();
  289 |   });
  290 | 
```