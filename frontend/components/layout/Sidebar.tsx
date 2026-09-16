"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";
import { api, type MeDto } from "@/lib/api";
import {
  LayoutDashboard,
  FolderKanban,
  BookOpen,
  Gauge,
  ScrollText,
  Menu,
  LogOut,
  Search,
} from "lucide-react";
import { LogoMark } from "@/components/brand/logo";
import { CommandPalette } from "./command-palette";

const nav = [
  { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard, exact: true },
  { href: "/projects", label: "Projects", icon: FolderKanban, exact: false },
  { href: "/knowledge", label: "Knowledge Base", icon: BookOpen, exact: false },
  { href: "/models", label: "Model Scorecard", icon: Gauge, exact: false },
  { href: "/audit", label: "Audit Trail", icon: ScrollText, exact: false },
];

function NavItems({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  return (
    <nav className="flex-1 space-y-1 p-3">
      <button
        type="button"
        onClick={() => window.dispatchEvent(new CustomEvent("experimento:open-command"))}
        className="mb-1 flex w-full items-center gap-2 rounded-md border border-input bg-background px-3 py-2 text-left text-sm text-muted-foreground"
        title="Open quick navigation (Ctrl+K)"
        data-testid="cmdk-trigger"
      >
        <Search className="h-4 w-4" />
        <span className="flex-1">Quick search…</span>
        <kbd className="rounded border px-1.5 py-0.5 text-[10px]">⌘K</kbd>
      </button>
      {nav.map((item) => {
        const Icon = item.icon;
        const active = item.exact
          ? pathname === item.href
          : pathname === item.href || pathname.startsWith(`${item.href}/`);
        return (
          <Link
            key={item.href}
            href={item.href}
            onClick={onNavigate}
            className={cn(
              "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
              active
                ? "bg-primary text-primary-foreground"
                : "text-muted-foreground hover:bg-muted hover:text-foreground"
            )}
          >
            <Icon className="h-4 w-4" />
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}

function UserFooter({ onNavigate }: { onNavigate?: () => void }) {
  const router = useRouter();
  const [me, setMe] = useState<MeDto | null>(null);

  useEffect(() => {
    api.getMe().then(setMe).catch(() => {});
  }, []);

  async function logout() {
    await api.logout().catch(() => {});
    router.push("/login");
  }

  return (
    <div className="border-t p-3">
      {me && <p className="truncate px-1 pb-2 text-xs text-muted-foreground">{me.email}</p>}
      <button
        type="button"
        onClick={() => {
          onNavigate?.();
          void logout();
        }}
        className="flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
      >
        <LogOut className="h-4 w-4" />
        Sign out
      </button>
    </div>
  );
}

// Единый каркас приложения: постоянный сайдбар на десктопе, верхняя панель с
// выезжающим меню на мобильных, плюс глобальная палитра навигации (Cmd/Ctrl+K).
export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const [drawerOpen, setDrawerOpen] = useState(false);

  // Меню закрывается при каждой смене маршрута.
  useEffect(() => setDrawerOpen(false), [pathname]);

  return (
    <div className="flex min-h-screen">
      {/* Десктопный сайдбар */}
      <aside className="hidden w-64 flex-col border-r bg-card md:flex">
        <div className="border-b p-5">
          <Link href="/dashboard" className="inline-flex items-center gap-2.5">
            <LogoMark className="h-8 w-8" />
            <span className="flex flex-col leading-none">
              <span className="text-lg font-extrabold tracking-tight">Experimento</span>
              <span className="mt-1 text-[11px] text-muted-foreground">AI Formulation Co-Pilot</span>
            </span>
          </Link>
        </div>
        <NavItems />
        <UserFooter />
      </aside>

      {/* Мобильная верхняя панель */}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center gap-3 border-b bg-card px-4 py-3 md:hidden">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            className="rounded-md p-1.5 text-muted-foreground hover:bg-muted"
            aria-label="Open navigation menu"
          >
            <Menu className="h-5 w-5" />
          </button>
          <Link href="/dashboard" className="inline-flex items-center gap-2">
            <LogoMark className="h-7 w-7" />
            <span className="text-base font-extrabold tracking-tight">Experimento</span>
          </Link>
        </header>

        <main className="flex-1 p-4 sm:p-6 md:p-8">{children}</main>
      </div>

      {/* Мобильный drawer */}
      {drawerOpen && (
        <div className="fixed inset-0 z-50 md:hidden">
          <div
            className="absolute inset-0 bg-black/40"
            onClick={() => setDrawerOpen(false)}
            aria-hidden
          />
          <aside className="absolute inset-y-0 left-0 flex w-72 max-w-[85vw] flex-col border-r bg-card shadow-lg">
            <div className="border-b p-5">
              <span className="text-lg font-extrabold tracking-tight">Experimento</span>
            </div>
            <NavItems onNavigate={() => setDrawerOpen(false)} />
            <UserFooter onNavigate={() => setDrawerOpen(false)} />
          </aside>
        </div>
      )}

      <CommandPalette />
    </div>
  );
}
