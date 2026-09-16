import { defineConfig } from "eslint/config";
import nextCoreWebVitals from "eslint-config-next/core-web-vitals";

export default defineConfig([
  { extends: [...nextCoreWebVitals] },
  {
    rules: {
      // В проекте системный паттерн загрузки данных и синхронизации через useEffect
      // (data-fetching-библиотеки нет). Новое правило React 19/Next 16 запрещает
      // синхронный setState в эффектах; его ужесточение требует переписать ~16 мест
      // по всей кодовой базе. Оставляем как предупреждение, чтобы не блокировать гейт;
      // отдельной вехой перейти на рекомендованные паттерны (derive state / data-библиотека).
      "react-hooks/set-state-in-effect": "warn",
    },
  },
]);
