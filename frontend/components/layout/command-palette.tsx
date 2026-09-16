"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { LayoutDashboard, FolderKanban, BookOpen, ScrollText, FlaskConical, Gauge, Search } from "lucide-react";
import { api, type ProjectDto, type FormulationDto } from "@/lib/api";

interface PaletteEntry {
  id: string;
  label: string;
  hint: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
}

const STATIC_ENTRIES: PaletteEntry[] = [
  { id: "nav-dashboard", label: "Dashboard", hint: "Page", href: "/dashboard", icon: LayoutDashboard },
  { id: "nav-projects", label: "Projects", hint: "Page", href: "/projects", icon: FolderKanban },
  { id: "nav-knowledge", label: "Knowledge Base", hint: "Page", href: "/knowledge", icon: BookOpen },
  { id: "nav-models", label: "Model Scorecard", hint: "Page", href: "/models", icon: Gauge },
  { id: "nav-audit", label: "Audit Trail", hint: "Page", href: "/audit", icon: ScrollText },
];

// Глобальная палитра быстрой навигации (Cmd/Ctrl+K): страницы + проекты + формуляции.
export function CommandPalette() {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [formulations, setFormulations] = useState<FormulationDto[]>([]);
  const [loaded, setLoaded] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setOpen((v) => !v);
      }
    };
    const onOpenEvent = () => setOpen(true);
    window.addEventListener("keydown", onKey);
    window.addEventListener("experimento:open-command", onOpenEvent);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("experimento:open-command", onOpenEvent);
    };
  }, []);

  // Данные сущностей подтягиваем при первом открытии.
  useEffect(() => {
    if (!open || loaded) return;
    void (async () => {
      const ps = await api.listProjects().catch(() => []);
      setProjects(ps);
      const fs = (
        await Promise.all(
          ps.map((p) => api.listFormulations(p.id).catch(() => [] as FormulationDto[]))
        )
      ).flat();
      setFormulations(fs);
      setLoaded(true);
    })();
  }, [open, loaded]);

  useEffect(() => {
    if (open) {
      setQuery("");
      setActive(0);
      setTimeout(() => inputRef.current?.focus(), 0);
    }
  }, [open]);

  const entries = useMemo<PaletteEntry[]>(() => {
    const projectEntries: PaletteEntry[] = projects.map((p) => ({
      id: `project-${p.id}`,
      label: p.name,
      hint: "Project",
      href: `/projects/${p.id}`,
      icon: FolderKanban,
    }));
    const formulationEntries: PaletteEntry[] = formulations.map((f) => ({
      id: `formulation-${f.id}`,
      label: f.name,
      hint: "Formulation",
      href: `/projects/${f.projectId}/formulations/${f.id}`,
      icon: FlaskConical,
    }));
    const all = [...STATIC_ENTRIES, ...projectEntries, ...formulationEntries];
    const q = query.trim().toLowerCase();
    if (!q) return all;
    return all.filter((e) => e.label.toLowerCase().includes(q));
  }, [projects, formulations, query]);

  const go = useCallback(
    (entry: PaletteEntry) => {
      setOpen(false);
      router.push(entry.href);
    },
    [router]
  );

  useEffect(() => setActive(0), [query]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-[60] flex items-start justify-center bg-black/40 p-4 pt-[15vh]"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) setOpen(false);
      }}
      role="dialog"
      aria-modal="true"
      aria-label="Quick navigation"
    >
      <div className="w-full max-w-lg overflow-hidden rounded-lg border bg-card shadow-lg">
        <div className="flex items-center gap-2 border-b px-4">
          <Search className="h-4 w-4 text-muted-foreground" />
          <input
            ref={inputRef}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Escape") setOpen(false);
              if (e.key === "ArrowDown") {
                e.preventDefault();
                setActive((a) => Math.min(a + 1, entries.length - 1));
              }
              if (e.key === "ArrowUp") {
                e.preventDefault();
                setActive((a) => Math.max(a - 1, 0));
              }
              if (e.key === "Enter" && entries[active]) go(entries[active]);
            }}
            placeholder="Search pages, projects, formulations…"
            className="h-12 flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
          />
          <kbd className="hidden rounded border px-1.5 py-0.5 text-[10px] text-muted-foreground sm:block">
            ESC
          </kbd>
        </div>
        <ul className="max-h-72 overflow-y-auto py-1">
          {entries.length === 0 && (
            <li className="px-4 py-6 text-center text-sm text-muted-foreground">Nothing found</li>
          )}
          {entries.map((e, i) => {
            const Icon = e.icon;
            return (
              <li key={e.id}>
                <button
                  type="button"
                  onMouseEnter={() => setActive(i)}
                  onClick={() => go(e)}
                  className={`flex w-full items-center gap-3 px-4 py-2 text-left text-sm ${
                    i === active ? "bg-muted" : ""
                  }`}
                >
                  <Icon className="h-4 w-4 text-muted-foreground" />
                  <span className="flex-1 truncate">{e.label}</span>
                  <span className="text-xs text-muted-foreground">{e.hint}</span>
                </button>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}
