"use client";

import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { FlaskConical, Brain, BookOpen, Search, Check, FileText } from "lucide-react";
import { cn } from "@/lib/utils";

const TAB_DURATION = 6800;

type SceneProps = { active: boolean; reduced: boolean };

/* ----------------------------- Сцена: Formulate ----------------------------- */

const FORMULATION_ROWS = [
  { name: "Acetylsalicylic acid", meta: "C9H8O4 · CID 2244", amount: "75.0%" },
  { name: "Microcrystalline cellulose", meta: "C6H10O5 · CID 147968", amount: "12.5%" },
  { name: "Stearic acid", meta: "C18H36O2 · CID 5281", amount: "2.0%" },
];

function FormulateScene({ active, reduced }: SceneProps) {
  const [phase, setPhase] = useState(reduced ? 3 : 0);
  const query = "C9H8O4";
  const typed = phase >= 1 ? (phase >= 2 ? query.length : 5) : 0;

  // Сброс фазы при активации/деактивации сцены или смене reduced — во время рендера.
  const sceneKey = `${active}:${reduced}`;
  const [prevSceneKey, setPrevSceneKey] = useState(sceneKey);
  if (sceneKey !== prevSceneKey) {
    setPrevSceneKey(sceneKey);
    setPhase(reduced ? 3 : 0);
  }

  // Таймеры анимации — единственный побочный эффект; setState происходит в колбэках.
  useEffect(() => {
    if (!active || reduced) return;
    const t = [
      setTimeout(() => setPhase(1), 500),
      setTimeout(() => setPhase(2), 1600),
      setTimeout(() => setPhase(3), 2900),
    ];
    return () => t.forEach(clearTimeout);
  }, [active, reduced]);

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-500" />
        <div className="flex h-10 items-center rounded-lg border border-white/10 bg-slate-950/80 pl-9 pr-3 font-mono text-sm text-cyan-300">
          {query.slice(0, typed)}
          <span className="lp-caret ml-0.5 inline-block h-4 w-[2px] bg-cyan-300" />
        </div>
        {phase === 1 && (
          <div className="absolute inset-x-0 top-11 z-10 overflow-hidden rounded-lg border border-white/10 bg-slate-900 shadow-2xl shadow-black/60">
            <div className="flex items-center justify-between px-3 py-2.5 text-sm hover:bg-white/5">
              <span className="text-slate-200">Acetylsalicylic acid</span>
              <span className="font-mono text-xs text-slate-500">C9H8O4</span>
            </div>
          </div>
        )}
      </div>

      {phase >= 2 && (
        <div className="flex flex-wrap gap-2">
          <span className="inline-flex items-center gap-1.5 rounded-full border border-cyan-400/30 bg-cyan-400/10 px-2.5 py-1 text-xs font-medium text-cyan-300">
            <Check className="h-3 w-3" /> Aspirin · CID 2244
          </span>
          <span className="inline-flex items-center rounded-full border border-white/10 bg-white/5 px-2.5 py-1 text-xs text-slate-400">
            + excipient
          </span>
        </div>
      )}

      <div className="space-y-2">
        {FORMULATION_ROWS.map((row, i) => {
          const shown = phase >= 3 || (phase >= 2 && i === 0);
          return (
            <div
              key={row.name}
              className={cn(
                "flex items-center justify-between rounded-lg border border-white/10 bg-white/[0.03] px-3 py-2.5 transition-all duration-500",
                shown ? "translate-y-0 opacity-100" : "translate-y-2 opacity-0"
              )}
              style={{ transitionDelay: `${i * 220}ms` }}
            >
              <div className="min-w-0">
                <p className="truncate text-sm text-slate-200">{row.name}</p>
                <p className="font-mono text-xs text-slate-500">{row.meta}</p>
              </div>
              <span className="ml-3 font-mono text-sm font-semibold text-cyan-300">{row.amount}</span>
            </div>
          );
        })}
      </div>

      {phase >= 3 && (
        <div className="flex items-center gap-2 rounded-lg border border-emerald-400/20 bg-emerald-400/[0.07] px-3 py-2 text-xs text-emerald-300">
          <Check className="h-3.5 w-3.5" /> PubChem lookup verified — every component has a real CID
        </div>
      )}
    </div>
  );
}

/* ------------------------------ Сцена: Predict ------------------------------ */

function PredictScene({ active, reduced }: SceneProps) {
  const [phase, setPhase] = useState(reduced ? 3 : 0);
  const R = 52;
  const CIRC = 2 * Math.PI * R;
  const TARGET = 0.82;

  // Сброс фазы при смене активности/reduced — корректировка состояния во время рендера.
  const sceneKey = `${active}:${reduced}`;
  const [prevSceneKey, setPrevSceneKey] = useState(sceneKey);
  if (sceneKey !== prevSceneKey) {
    setPrevSceneKey(sceneKey);
    setPhase(reduced ? 3 : 0);
  }

  useEffect(() => {
    if (!active || reduced) return;
    const t = [
      setTimeout(() => setPhase(1), 900),
      setTimeout(() => setPhase(2), 2400),
      setTimeout(() => setPhase(3), 3600),
    ];
    return () => t.forEach(clearTimeout);
  }, [active, reduced]);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-4">
        <div className="relative h-32 w-32 shrink-0">
          <svg viewBox="0 0 120 120" className="h-full w-full -rotate-90">
            <circle cx="60" cy="60" r={R} fill="none" stroke="rgb(255 255 255 / 0.08)" strokeWidth="9" />
            <circle
              cx="60"
              cy="60"
              r={R}
              fill="none"
              stroke="url(#lp-gauge)"
              strokeWidth="9"
              strokeLinecap="round"
              strokeDasharray={CIRC}
              strokeDashoffset={phase >= 1 ? CIRC * (1 - TARGET) : CIRC}
              style={{ transition: "stroke-dashoffset 1.4s cubic-bezier(0.22,1,0.36,1)" }}
            />
            <defs>
              <linearGradient id="lp-gauge" x1="0" y1="0" x2="120" y2="120">
                <stop stopColor="#2DD4BF" />
                <stop offset="1" stopColor="#22B8E8" />
              </linearGradient>
            </defs>
          </svg>
          <div className="absolute inset-0 flex flex-col items-center justify-center">
            <span className="text-2xl font-extrabold text-slate-100">
              {phase >= 1 ? TARGET.toFixed(2) : "—"}
            </span>
            <span className="text-[10px] uppercase tracking-wider text-slate-500">success</span>
          </div>
        </div>
        <div className="min-w-0 flex-1 space-y-2">
          {phase === 0 && (
            <div className="relative overflow-hidden rounded-lg border border-white/10 bg-slate-950/80 px-3 py-3 text-xs text-slate-400">
              <div className="lp-scanline absolute inset-x-0 h-1/4 bg-gradient-to-b from-transparent via-cyan-400/10 to-transparent" />
              Running rule-based-v2 over 14 descriptors…
            </div>
          )}
          {[
            { label: "Skin irritation", value: "Low", tone: "emerald" },
            { label: "Thermal stability", value: "High", tone: "emerald" },
            { label: "Solubility risk", value: "Watch", tone: "amber" },
          ].map((chip, i) => (
            <div
              key={chip.label}
              className={cn(
                "flex items-center justify-between rounded-md border px-3 py-1.5 text-xs transition-all duration-500",
                chip.tone === "emerald" && "border-emerald-400/20 bg-emerald-400/[0.06] text-emerald-300",
                chip.tone === "amber" && "border-amber-400/20 bg-amber-400/[0.06] text-amber-300",
                phase >= 2 ? "translate-y-0 opacity-100" : "translate-y-2 opacity-0"
              )}
              style={{ transitionDelay: `${i * 220}ms` }}
            >
              <span className="text-slate-400">{chip.label}</span>
              <span className="font-semibold">{chip.value}</span>
            </div>
          ))}
        </div>
      </div>
      <div
        className={cn(
          "rounded-lg border border-white/10 bg-white/[0.03] p-3 text-xs leading-relaxed text-slate-400 transition-opacity duration-700",
          phase >= 3 ? "opacity-100" : "opacity-0"
        )}
      >
        <span className="font-semibold text-slate-300">Rationale:</span> aromatic ester contributes low
        volatility; predicted LogP 1.2 supports transdermal uptake.{" "}
        <span className="font-mono text-slate-500">model=rule-based-v2</span>
      </div>
    </div>
  );
}

/* ----------------------------- Сцена: Knowledge ----------------------------- */

const KB_RESULTS = [
  { title: "solubility-study.csv", text: "compound: Caffeine; solvent: water; mg_per_ml: 20", score: 0.91 },
  { title: "lab-notes.xlsx · Sheet 2", text: "compound: Caffeine; note: fully soluble at 80 °C", score: 0.87 },
  { title: "paper.docx", text: "Polar solvents accelerate extraction of methylxanthines…", score: 0.82 },
];

function KnowledgeScene({ active, reduced }: SceneProps) {
  const [phase, setPhase] = useState(reduced ? 2 : 0);
  const query = "which solvent dissolves caffeine best?";
  const typed = phase === 0 ? Math.min(query.length, 24) : query.length;

  // Сброс фазы при смене активности/reduced — корректировка состояния во время рендера.
  const sceneKey = `${active}:${reduced}`;
  const [prevSceneKey, setPrevSceneKey] = useState(sceneKey);
  if (sceneKey !== prevSceneKey) {
    setPrevSceneKey(sceneKey);
    setPhase(reduced ? 2 : 0);
  }

  useEffect(() => {
    if (!active || reduced) return;
    const t = [
      setTimeout(() => setPhase(1), 1500),
      setTimeout(() => setPhase(2), 2800),
    ];
    return () => t.forEach(clearTimeout);
  }, [active, reduced]);

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-500" />
        <div className="flex h-10 items-center rounded-lg border border-white/10 bg-slate-950/80 pl-9 pr-3 text-sm text-slate-200">
          <span className="truncate">{query.slice(0, typed)}</span>
          {phase === 0 && <span className="lp-caret ml-0.5 inline-block h-4 w-[2px] shrink-0 bg-cyan-300" />}
        </div>
      </div>

      {phase === 1 && (
        <div className="flex items-center gap-2 px-1 text-xs text-slate-500">
          <span className="lp-pulse-dot h-1.5 w-1.5 rounded-full bg-cyan-400" />
          vectorizing · 1536-dim embedding · searching pgvector
        </div>
      )}

      <div className="space-y-2">
        {KB_RESULTS.map((r, i) => (
          <div
            key={r.title}
            className={cn(
              "rounded-lg border border-white/10 bg-white/[0.03] p-3 transition-all duration-500",
              phase >= 2 ? "translate-y-0 opacity-100" : "translate-y-3 opacity-0"
            )}
            style={{ transitionDelay: `${i * 260}ms` }}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="flex items-center gap-1.5 truncate text-xs font-semibold text-slate-200">
                <FileText className="h-3.5 w-3.5 shrink-0 text-cyan-400" />
                {r.title}
              </span>
              <span className="shrink-0 font-mono text-[11px] text-cyan-300">{r.score.toFixed(2)}</span>
            </div>
            <p className="mt-1 truncate font-mono text-[11px] text-slate-500">{r.text}</p>
            <div className="mt-2 h-1 overflow-hidden rounded-full bg-white/5">
              <div
                className="h-full rounded-full bg-gradient-to-r from-teal-400 to-cyan-400 transition-all duration-1000"
                style={{ width: phase >= 2 ? `${r.score * 100}%` : "0%", transitionDelay: `${i * 260 + 200}ms` }}
              />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/* -------------------------------- Каркас -------------------------------- */

const TABS = [
  { id: "formulate", label: "Formulate", icon: FlaskConical, Scene: FormulateScene },
  { id: "predict", label: "Predict", icon: Brain, Scene: PredictScene },
  { id: "knowledge", label: "Knowledge", icon: BookOpen, Scene: KnowledgeScene },
] as const;

const REDUCED_MOTION_QUERY = "(prefers-reduced-motion: reduce)";

// Подписка на системную настройку reduced motion без setState в эффекте.
function subscribeReducedMotion(callback: () => void) {
  const mq = window.matchMedia(REDUCED_MOTION_QUERY);
  mq.addEventListener("change", callback);
  return () => mq.removeEventListener("change", callback);
}

export function HeroDemo() {
  const [tab, setTab] = useState(0);
  const reduced = useSyncExternalStore(
    subscribeReducedMotion,
    () => window.matchMedia(REDUCED_MOTION_QUERY).matches,
    () => false
  );
  const timer = useRef<ReturnType<typeof setInterval> | null>(null);

  // Автопрокрутка вкладок; setState выполняется только в колбэке интервала.
  useEffect(() => {
    if (reduced) return;
    timer.current = setInterval(() => setTab((t) => (t + 1) % TABS.length), TAB_DURATION);
    return () => {
      if (timer.current) clearInterval(timer.current);
    };
  }, [reduced]);

  function pick(i: number) {
    setTab(i);
    if (timer.current) {
      clearInterval(timer.current);
      if (!reduced) timer.current = setInterval(() => setTab((t) => (t + 1) % TABS.length), TAB_DURATION);
    }
  }

  return (
    <div className="relative">
      <div className="absolute -inset-4 -z-10 rounded-[2rem] bg-gradient-to-br from-teal-500/15 via-cyan-500/10 to-blue-500/10 blur-2xl" />
      <div className="overflow-hidden rounded-2xl border border-white/10 bg-slate-900/80 shadow-2xl shadow-black/50 backdrop-blur">
        {/* Window chrome */}
        <div className="flex items-center gap-2 border-b border-white/10 px-4 py-3">
          <span className="h-2.5 w-2.5 rounded-full bg-rose-400/70" />
          <span className="h-2.5 w-2.5 rounded-full bg-amber-400/70" />
          <span className="h-2.5 w-2.5 rounded-full bg-emerald-400/70" />
          <div className="ml-3 flex h-6 flex-1 items-center rounded-md bg-white/5 px-3 font-mono text-[11px] text-slate-500">
            app.experimento.io
          </div>
          <span className="hidden items-center gap-1.5 text-[11px] text-emerald-300 sm:flex">
            <span className="lp-pulse-dot h-1.5 w-1.5 rounded-full bg-emerald-400" /> live demo
          </span>
        </div>

        {/* Tabs */}
        <div className="flex gap-1 border-b border-white/10 px-3 pt-2">
          {TABS.map((t, i) => {
            const Icon = t.icon;
            return (
              <button
                key={t.id}
                type="button"
                onClick={() => pick(i)}
                className={cn(
                  "relative flex items-center gap-2 rounded-t-md px-3 py-2 text-xs font-medium transition-colors sm:text-sm",
                  tab === i ? "text-cyan-300" : "text-slate-500 hover:text-slate-300"
                )}
              >
                <Icon className="h-3.5 w-3.5" />
                {t.label}
                {tab === i && <span className="absolute inset-x-2 -bottom-px h-0.5 rounded-full bg-cyan-400" />}
              </button>
            );
          })}
        </div>

        <div className="p-4 sm:p-5">
          {TABS.map((t, i) => (
            <div key={t.id} className={cn(tab === i ? "block" : "hidden")}>
              <t.Scene active={tab === i} reduced={reduced} />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
