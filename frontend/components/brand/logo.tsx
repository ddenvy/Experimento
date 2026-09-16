import { cn } from "@/lib/utils";

/**
 * Фирменный знак Experimento: колба Эрленмейера с «пузырьками-молекулами»
 * на градиентной плашке. Чистый SVG, без внешних ассетов.
 */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 48 48" fill="none" className={cn("h-9 w-9", className)} aria-hidden="true">
      <defs>
        <linearGradient id="experimento-logo-gradient" x1="6" y1="4" x2="42" y2="44" gradientUnits="userSpaceOnUse">
          <stop stopColor="#2DD4BF" />
          <stop offset="0.55" stopColor="#22B8E8" />
          <stop offset="1" stopColor="#3B82F6" />
        </linearGradient>
      </defs>
      <rect x="2" y="2" width="44" height="44" rx="12" fill="url(#experimento-logo-gradient)" />
      {/* Колба */}
      <path
        d="M21.4 11.5h5.2v7.1l8.7 15.0a2.9 2.9 0 0 1-2.51 4.4H15.2a2.9 2.9 0 0 1-2.51-4.4l8.7-15v-7.1Z"
        fill="white"
      />
      {/* Пузырьки */}
      <circle cx="21.4" cy="32.8" r="1.7" fill="#0E7490" />
      <circle cx="26.6" cy="28.6" r="2.2" fill="#0891B2" />
      <circle cx="25.2" cy="34.6" r="1.3" fill="#0E7490" />
    </svg>
  );
}

export function Logo({
  className,
  wordmarkClassName,
  showTagline = false,
}: {
  className?: string;
  wordmarkClassName?: string;
  showTagline?: boolean;
}) {
  return (
    <span className={cn("inline-flex items-center gap-2.5", className)}>
      <LogoMark />
      <span className="flex flex-col leading-none">
        <span className={cn("text-lg font-extrabold tracking-tight", wordmarkClassName)}>Experimento</span>
        {showTagline && <span className="text-[11px] font-medium opacity-60 mt-1">AI Formulation Co-Pilot</span>}
      </span>
    </span>
  );
}
