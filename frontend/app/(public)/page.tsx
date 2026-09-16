import Link from "next/link";
import {
  FlaskConical,
  Brain,
  Activity,
  BookOpen,
  ShieldCheck,
  Search,
  ArrowRight,
  FileText,
  Lock,
  KeyRound,
  ListChecks,
} from "lucide-react";
import { LogoMark } from "@/components/brand/logo";
import { HeroDemo } from "@/components/marketing/hero-demo";
import { Reveal } from "@/components/marketing/reveal";

export const metadata = {
  title: "Experimento — AI Formulation & R&D Co-Pilot",
  description:
    "Compose formulations from a PubChem-verified catalog, predict properties with explainable AI, and keep every decision in an immutable, regulator-ready audit trail.",
};

const NAV_LINKS = [
  { href: "#features", label: "Platform" },
  { href: "#how", label: "How it works" },
  { href: "#trust", label: "Trust" },
];

const SIGNALS = [
  "PubChem CID on every component",
  "1536-dim semantic vectors",
  "SHA-256 hash chain",
  "7 file formats indexed",
  "model + version per prediction",
];

function StartFree({ children = "Start free", className = "" }: { children?: React.ReactNode; className?: string }) {
  return (
    <Link
      href="/login"
      className={`inline-flex items-center justify-center gap-2 rounded-lg bg-white px-5 py-3 text-sm font-semibold text-slate-950 transition-all hover:bg-cyan-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-cyan-300 ${className}`}
    >
      {children}
      <ArrowRight className="h-4 w-4" />
    </Link>
  );
}

/* --------------------------------- Навбар --------------------------------- */

function Navbar() {
  return (
    <header className="absolute inset-x-0 top-0 z-30">
      <nav className="mx-auto flex max-w-7xl items-center justify-between px-5 py-5 md:px-8">
        <Link href="/" className="flex items-center gap-2.5" aria-label="Experimento home">
          <LogoMark className="h-9 w-9" />
          <span className="text-lg font-extrabold tracking-tight text-white">Experimento</span>
        </Link>
        <div className="hidden items-center gap-8 md:flex">
          {NAV_LINKS.map((l) => (
            <a
              key={l.href}
              href={l.href}
              className="text-sm font-medium text-slate-400 transition-colors hover:text-white"
            >
              {l.label}
            </a>
          ))}
        </div>
        <Link
          href="/login"
          className="rounded-lg border border-white/15 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-white/10 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-cyan-300"
        >
          Sign in
        </Link>
      </nav>
    </header>
  );
}

/* ---------------------------------- Hero ---------------------------------- */

function Hero() {
  return (
    <section className="relative overflow-hidden pt-32 pb-20 md:pt-40 md:pb-28">
      {/* Фон: сетка + свечения */}
      <div className="lp-grid pointer-events-none absolute inset-0 [mask-image:radial-gradient(ellipse_75%_60%_at_50%_0%,black,transparent)]" />
      <div className="pointer-events-none absolute -top-40 left-1/2 h-[520px] w-[820px] -translate-x-1/2 rounded-full bg-cyan-500/15 blur-[140px]" />
      <div className="pointer-events-none absolute right-[-120px] top-40 h-72 w-72 rounded-full bg-teal-500/10 blur-[100px] lp-float" />
      <div className="pointer-events-none absolute left-[-100px] top-72 h-64 w-64 rounded-full bg-blue-500/10 blur-[100px] lp-float" style={{ animationDelay: "-4s" }} />

      <div className="relative mx-auto grid max-w-7xl items-center gap-14 px-5 md:px-8 lg:grid-cols-[1.05fr_0.95fr]">
        <div>
          <div className="inline-flex items-center gap-2 rounded-full border border-cyan-400/20 bg-cyan-400/[0.08] px-3 py-1 text-xs font-medium text-cyan-300">
            <FlaskConical className="h-3.5 w-3.5" />
            AI Co-Pilot for formulation R&amp;D
          </div>
          <h1 className="mt-6 text-4xl font-extrabold leading-[1.08] tracking-tight text-white sm:text-5xl lg:text-[3.6rem]">
            Formulate with evidence.{" "}
            <span className="bg-gradient-to-r from-teal-300 via-cyan-300 to-sky-400 bg-clip-text text-transparent">
              Predict before you mix.
            </span>{" "}
            Prove every step.
          </h1>
          <p className="mt-6 max-w-xl text-lg leading-relaxed text-slate-400">
            Experimento lets R&amp;D teams compose formulations from a PubChem-verified catalog, predict
            properties with explainable AI, stress-test batches in simulation, and keep every decision in an
            immutable, regulator-ready audit trail.
          </p>
          <div className="mt-9 flex flex-col gap-3 sm:flex-row sm:items-center">
            <StartFree />
            <a
              href="#how"
              className="inline-flex items-center justify-center gap-2 rounded-lg border border-white/15 px-5 py-3 text-sm font-semibold text-white transition-colors hover:bg-white/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-cyan-300"
            >
              See how it works
            </a>
          </div>
          <p className="mt-5 font-mono text-xs text-slate-500">
            PubChem-verified catalog · immutable audit trail · no lab coat required to try
          </p>
        </div>

        <Reveal delay={150}>
          <HeroDemo />
        </Reveal>
      </div>
    </section>
  );
}

/* ------------------------------ Proof signals ----------------------------- */

function SignalBand() {
  return (
    <section className="border-y border-white/5 bg-white/[0.02]">
      <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-center gap-x-8 gap-y-3 px-5 py-5 md:justify-between md:px-8">
        {SIGNALS.map((s) => (
          <span key={s} className="font-mono text-xs uppercase tracking-wider text-slate-500">
            {s}
          </span>
        ))}
      </div>
    </section>
  );
}

/* --------------------------------- Features -------------------------------- */

function SectionHeading({
  eyebrow,
  title,
  copy,
}: {
  eyebrow: string;
  title: string;
  copy: string;
}) {
  return (
    <div className="max-w-2xl">
      <p className="font-mono text-xs font-semibold uppercase tracking-[0.2em] text-cyan-400">{eyebrow}</p>
      <h2 className="mt-3 text-3xl font-extrabold tracking-tight text-white sm:text-4xl">{title}</h2>
      <p className="mt-4 text-lg text-slate-400">{copy}</p>
    </div>
  );
}

function CardShell({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`group relative overflow-hidden rounded-2xl border border-white/10 bg-gradient-to-b from-white/[0.05] to-white/[0.015] p-7 transition-colors hover:border-cyan-400/30 ${className}`}
    >
      {children}
    </div>
  );
}

function BuilderVisual() {
  return (
    <div className="mt-7 space-y-2.5">
      <div className="flex items-center gap-2 rounded-lg border border-white/10 bg-slate-950/70 px-3 py-2.5">
        <Search className="h-4 w-4 shrink-0 text-slate-500" />
        <span className="font-mono text-sm text-slate-400">aspirin · 50-78-2 · C9H8O4</span>
      </div>
      <div className="flex flex-wrap gap-2">
        {["Aspirin · CID 2244", "Cellulose · CID 147968", "Stearic acid · CID 5281"].map((c) => (
          <span
            key={c}
            className="rounded-full border border-cyan-400/20 bg-cyan-400/[0.07] px-2.5 py-1 font-mono text-[11px] text-cyan-300"
          >
            {c}
          </span>
        ))}
      </div>
      <div className="flex h-3 overflow-hidden rounded-full">
        <span className="bg-gradient-to-r from-teal-400 to-cyan-400" style={{ width: "75%" }} />
        <span className="bg-sky-500/70" style={{ width: "13%" }} />
        <span className="bg-indigo-500/50" style={{ width: "12%" }} />
      </div>
    </div>
  );
}

function AuditVisual() {
  const rows = [
    { action: "Knowledge.UploadFile", hash: "9f3a…c0e9" },
    { action: "Prediction.Run", hash: "4b21…77da" },
    { action: "Formulation.Publish", hash: "aa10…5e42" },
  ];
  return (
    <div className="mt-7 space-y-2">
      {rows.map((r, i) => (
        <div key={r.action} className="flex items-center justify-between rounded-lg border border-white/10 bg-slate-950/60 px-3 py-2">
          <span className="flex min-w-0 items-center gap-2 font-mono text-[11px] text-slate-400">
            <Lock className="h-3 w-3 shrink-0 text-emerald-400" />
            <span className="truncate">{r.action}</span>
          </span>
          <span className="font-mono text-[11px] text-slate-600">{r.hash}</span>
          <span className="sr-only">row {i + 1}</span>
        </div>
      ))}
      <p className="flex items-center gap-1.5 pt-1 text-[11px] text-emerald-300">
        <ShieldCheck className="h-3.5 w-3.5" /> chain verified — ALCOA+ intact
      </p>
    </div>
  );
}

function PredictVisual() {
  return (
    <div className="mt-7">
      <div className="flex items-end justify-between text-xs text-slate-400">
        <span>Predicted success</span>
        <span className="font-mono text-base font-bold text-cyan-300">0.82</span>
      </div>
      <div className="mt-2 h-2 overflow-hidden rounded-full bg-white/5">
        <div className="h-full w-[82%] rounded-full bg-gradient-to-r from-teal-400 to-cyan-400" />
      </div>
      <div className="mt-3 flex flex-wrap gap-1.5">
        <span className="rounded border border-emerald-400/20 bg-emerald-400/[0.07] px-2 py-0.5 text-[10px] text-emerald-300">
          irritation: low
        </span>
        <span className="rounded border border-amber-400/20 bg-amber-400/[0.07] px-2 py-0.5 text-[10px] text-amber-300">
          solubility: watch
        </span>
      </div>
      <p className="mt-3 border-l-2 border-cyan-400/40 pl-2.5 text-[11px] leading-relaxed text-slate-500">
        Every score ships with a rationale and the exact model version that produced it.
      </p>
    </div>
  );
}

function KnowledgeVisual() {
  return (
    <div className="mt-7 space-y-3">
      <div className="flex flex-wrap gap-1.5">
        {["PDF", "DOCX", "TXT", "MD", "CSV", "XLS", "XLSX"].map((f) => (
          <span
            key={f}
            className="rounded border border-white/10 bg-white/5 px-2 py-1 font-mono text-[10px] font-semibold text-slate-300"
          >
            {f}
          </span>
        ))}
      </div>
      <div className="flex items-center gap-2 rounded-lg border border-white/10 bg-slate-950/70 px-3 py-2.5">
        <FileText className="h-4 w-4 shrink-0 text-cyan-400" />
        <span className="truncate font-mono text-xs text-slate-400">compound: Caffeine; solvent: water; mg/ml: 20</span>
        <span className="ml-auto shrink-0 font-mono text-[11px] text-cyan-300">0.91</span>
      </div>
      <p className="text-[11px] text-slate-500">
        Spreadsheets are parsed row by row — each record becomes searchable knowledge.
      </p>
    </div>
  );
}

function SimulateVisual() {
  const bars = [38, 52, 61, 74, 88, 96, 84, 66, 49, 34, 42, 58];
  return (
    <div className="mt-7 flex items-end gap-1.5" aria-hidden="true">
      {bars.map((h, i) => (
        <div
          key={i}
          className="flex-1 rounded-t bg-gradient-to-t from-cyan-500/30 to-cyan-300/80"
          style={{ height: `${h * 0.9}px` }}
        />
      ))}
    </div>
  );
}

function Features() {
  return (
    <section id="features" className="relative scroll-mt-20 py-24 md:py-32">
      <div className="mx-auto max-w-7xl px-5 md:px-8">
        <Reveal>
          <SectionHeading
            eyebrow="The platform"
            title="One workspace from first hypothesis to audit-ready evidence"
            copy="Five capabilities that replace the chain of spreadsheets, shared drives and notebooks R&amp;D teams normally stitch together."
          />
        </Reveal>

        <div className="mt-14 grid gap-5 lg:grid-cols-3">
          <Reveal className="lg:col-span-2">
            <CardShell className="h-full">
              <FlaskConical className="h-6 w-6 text-cyan-300" />
              <h3 className="mt-4 text-xl font-bold text-white">Verified formulation builder</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-400">
                Search the chemical catalog by name, CAS number or molecular formula. Every component carries a
                live PubChem CID, so a typo can never silently turn into a wrong ingredient. Versions,
                percentages and excipients are tracked from draft to release.
              </p>
              <BuilderVisual />
            </CardShell>
          </Reveal>

          <Reveal delay={100}>
            <CardShell className="h-full">
              <ShieldCheck className="h-6 w-6 text-emerald-300" />
              <h3 className="mt-4 text-xl font-bold text-white">Immutable audit trail</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-400">
                Each action is hash-chained and timestamped. Tampering is detectable in one click — the chain
                either verifies end to end or it doesn&apos;t.
              </p>
              <AuditVisual />
            </CardShell>
          </Reveal>

          <Reveal>
            <CardShell className="h-full">
              <Brain className="h-6 w-6 text-cyan-300" />
              <h3 className="mt-4 text-xl font-bold text-white">Predictions with rationale</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-400">
                Success probability and hazard bands before a single gram is mixed — each result explained and
                tagged with the model version behind it.
              </p>
              <PredictVisual />
            </CardShell>
          </Reveal>

          <Reveal delay={100} className="lg:col-span-2">
            <CardShell className="h-full">
              <BookOpen className="h-6 w-6 text-cyan-300" />
              <h3 className="mt-4 text-xl font-bold text-white">Knowledge base that reads your documents</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-400">
                Upload papers, lab notes and tables — PDF, DOCX, TXT, Markdown, CSV and Excel — and ask
                questions in plain language. Rows, sheets and paragraphs are embedded into semantic vectors, so
                &quot;solvent for caffeine&quot; finds the right spreadsheet even when the word
                &quot;dissolve&quot; never appears in it.
              </p>
              <KnowledgeVisual />
            </CardShell>
          </Reveal>

          <Reveal className="lg:col-span-3">
            <CardShell>
              <div className="grid items-center gap-6 md:grid-cols-[1fr_1.1fr]">
                <div>
                  <Activity className="h-6 w-6 text-cyan-300" />
                  <h3 className="mt-4 text-xl font-bold text-white">Stress-test batches with simulation</h3>
                  <p className="mt-2 text-sm leading-relaxed text-slate-400">
                    Monte-Carlo runs sweep uncertainty across ingredient tolerances and process parameters,
                    surfacing failure tails before they surface in production. Every run is linked to the
                    formulation version it tested.
                  </p>
                </div>
                <SimulateVisual />
              </div>
            </CardShell>
          </Reveal>
        </div>
      </div>
    </section>
  );
}

/* -------------------------------- How it works ----------------------------- */

const STEPS = [
  {
    icon: BookOpen,
    title: "Capture knowledge",
    copy: "Upload papers, SOPs and lab tables. They become a searchable semantic base for the whole team.",
  },
  {
    icon: ListChecks,
    title: "Compose & verify",
    copy: "Build a formulation from PubChem-verified components, with percentages and versions under control.",
  },
  {
    icon: Brain,
    title: "Predict & stress-test",
    copy: "Get property predictions with explanations, then run simulations against parameter uncertainty.",
  },
  {
    icon: KeyRound,
    title: "Record & defend",
    copy: "Every decision lands in a hash-chained ALCOA+ trail — ready for review long after the team forgets why.",
  },
];

function HowItWorks() {
  return (
    <section id="how" className="relative scroll-mt-20 border-t border-white/5 bg-white/[0.02] py-24 md:py-32">
      <div className="mx-auto max-w-7xl px-5 md:px-8">
        <Reveal>
          <SectionHeading
            eyebrow="How it works"
            title="From raw documents to defensible decisions"
            copy="A loop, not a stack of tools — what the team learns feeds the next formulation."
          />
        </Reveal>
        <ol className="mt-14 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
          {STEPS.map((s, i) => {
            const Icon = s.icon;
            return (
              <Reveal as="li" key={s.title} delay={i * 90} className="list-none">
                <div className="relative h-full rounded-2xl border border-white/10 bg-white/[0.03] p-6">
                  <span className="absolute right-5 top-4 font-mono text-3xl font-extrabold text-white/10">
                    0{i + 1}
                  </span>
                  <span className="inline-flex h-10 w-10 items-center justify-center rounded-lg border border-cyan-400/20 bg-cyan-400/[0.08]">
                    <Icon className="h-5 w-5 text-cyan-300" />
                  </span>
                  <h3 className="mt-4 text-base font-bold text-white">{s.title}</h3>
                  <p className="mt-2 text-sm leading-relaxed text-slate-400">{s.copy}</p>
                </div>
              </Reveal>
            );
          })}
        </ol>
      </div>
    </section>
  );
}

/* ---------------------------------- Trust --------------------------------- */

function Trust() {
  return (
    <section id="trust" className="relative scroll-mt-20 py-24 md:py-32">
      <div className="mx-auto max-w-7xl px-5 md:px-8">
        <Reveal>
          <SectionHeading
            eyebrow="Trust & traceability"
            title="Built for the question regulators actually ask"
            copy="Not 'what did the AI say?' — but 'who decided what, based on which data and which model version, and can you prove nobody changed it?'"
          />
        </Reveal>
        <div className="mt-14 grid gap-5 md:grid-cols-3">
          {[
            {
              icon: Lock,
              title: "Tamper-evident by construction",
              copy: "SHA-256 hash chaining links every audit entry. One integrity check proves the whole history.",
            },
            {
              icon: ShieldCheck,
              title: "ALCOA+ aligned",
              copy: "Attributable, legible, contemporaneous, original and accurate records — with who, when and why attached.",
            },
            {
              icon: KeyRound,
              title: "Session security by default",
              copy: "Refresh tokens live in httpOnly cookies; access tokens never touch storage. Access is scoped per project.",
            },
          ].map((t, i) => {
            const Icon = t.icon;
            return (
              <Reveal key={t.title} delay={i * 100}>
                <CardShell className="h-full">
                  <Icon className="h-6 w-6 text-teal-300" />
                  <h3 className="mt-4 text-lg font-bold text-white">{t.title}</h3>
                  <p className="mt-2 text-sm leading-relaxed text-slate-400">{t.copy}</p>
                </CardShell>
              </Reveal>
            );
          })}
        </div>
      </div>
    </section>
  );
}

/* -------------------------------- Final CTA -------------------------------- */

function FinalCta() {
  return (
    <section className="relative overflow-hidden border-t border-white/5 py-24 md:py-32">
      <div className="pointer-events-none absolute left-1/2 top-1/2 h-80 w-[680px] -translate-x-1/2 -translate-y-1/2 rounded-full bg-cyan-500/15 blur-[130px]" />
      <Reveal className="relative mx-auto max-w-3xl px-5 text-center md:px-8">
        <h2 className="text-3xl font-extrabold tracking-tight text-white sm:text-5xl">
          Your next formulation should leave a paper trail.
        </h2>
        <p className="mt-5 text-lg text-slate-400">
          Sign in and run a formulation, a prediction and a semantic search end to end — the audit chain records
          every click.
        </p>
        <div className="mt-9 flex justify-center">
          <StartFree className="px-7 py-3.5 text-base" />
        </div>
      </Reveal>
    </section>
  );
}

/* --------------------------------- Footer --------------------------------- */

function Footer() {
  return (
    <footer className="border-t border-white/5 py-10">
      <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-4 px-5 md:flex-row md:px-8">
        <div className="flex items-center gap-2.5">
          <LogoMark className="h-7 w-7" />
          <span className="text-sm font-bold text-white">Experimento</span>
          <span className="text-xs text-slate-600">· AI Formulation Co-Pilot</span>
        </div>
        <nav className="flex items-center gap-6 text-xs text-slate-500">
          {NAV_LINKS.map((l) => (
            <a key={l.href} href={l.href} className="transition-colors hover:text-slate-300">
              {l.label}
            </a>
          ))}
          <Link href="/login" className="transition-colors hover:text-slate-300">
            Sign in
          </Link>
        </nav>
      </div>
    </footer>
  );
}

export default function LandingPage() {
  return (
    <div className="min-h-screen overflow-x-hidden bg-[#060A13] text-slate-200 selection:bg-cyan-400/30">
      <Navbar />
      <main>
        <Hero />
        <SignalBand />
        <Features />
        <HowItWorks />
        <Trust />
        <FinalCta />
      </main>
      <Footer />
    </div>
  );
}
