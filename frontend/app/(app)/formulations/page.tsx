import { redirect } from "next/navigation";

// Инструменты формуляций переехали в разрезы сущностей (Projects → Formulation).
export default function FormulationsRedirect() {
  redirect("/projects");
}
