import { redirect } from "next/navigation";

// Прогнозы выполняются из детальной страницы формуляции (вкладка Predictions).
export default function PredictionsRedirect() {
  redirect("/projects");
}
