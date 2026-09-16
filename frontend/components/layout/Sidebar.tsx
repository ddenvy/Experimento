"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";
import {
  LayoutDashboard,
  FlaskConical,
  Brain,
  Activity,
  BookOpen,
  ScrollText,
} from "lucide-react";

const nav = [
  { href: "/", label: "Dashboard", icon: LayoutDashboard },
  { href: "/formulations", label: "Formulations", icon: FlaskConical },
  { href: "/predictions", label: "Predictions", icon: Brain },
  { href: "/simulations", label: "Simulations", icon: Activity },
  { href: "/knowledge", label: "Knowledge Base", icon: BookOpen },
  { href: "/audit", label: "Audit Trail", icon: ScrollText },
];

export function Sidebar() {
  const pathname = usePathname();
  return (
    <aside className="hidden md:flex w-64 flex-col border-r bg-card">
      <div className="p-6 border-b">
        <h1 className="text-xl font-bold tracking-tight">Experimento</h1>
        <p className="text-xs text-muted-foreground mt-1">AI Formulation Co-Pilot</p>
      </div>
      <nav className="flex-1 p-3 space-y-1">
        {nav.map((item) => {
          const Icon = item.icon;
          const active = pathname === item.href || (item.href !== "/" && pathname.startsWith(item.href));
          return (
            <Link
              key={item.href}
              href={item.href}
              className={cn(
                "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                active ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted hover:text-foreground"
              )}
            >
              <Icon className="h-4 w-4" />
              {item.label}
            </Link>
          );
        })}
      </nav>
      <div className="p-4 border-t text-xs text-muted-foreground">
        <p>Model: rule-based-v1</p>
        <p className="mt-1">FDA/EMA traceable</p>
      </div>
    </aside>
  );
}
