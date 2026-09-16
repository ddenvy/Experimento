import { defineConfig } from "eslint/config";
import nextCoreWebVitals from "eslint-config-next/core-web-vitals";

export default defineConfig([
  {
    // Генерируемые артефакты Playwright содержат минифицированные бандлы
    // трейс-вьюера — линтить их бессмысленно.
    ignores: ["playwright-report/**", "test-results/**"],
  },
  { extends: [...nextCoreWebVitals] },
]);
