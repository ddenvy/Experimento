import { redirect } from "next/navigation";

// Симуляции выполняются из детальной страницы формуляции (вкладка Simulations).
export default function SimulationsRedirect() {
  redirect("/projects");
}
