"use client";

import { useEffect, useLayoutEffect, useRef } from "react";
import { cn } from "@/lib/utils";

const REDUCED_MOTION_QUERY = "(prefers-reduced-motion: reduce)";

/**
 * Появление блока при скролле. Fail-open: без JS (или при reduced-motion)
 * контент виден сразу — класс скрытия выставляется императивно в DOM
 * в layout-эффекте до первой отрисовки, без React-состояния.
 */
export function Reveal({
  children,
  className,
  delay = 0,
  as: Tag = "div",
}: {
  children: React.ReactNode;
  className?: string;
  delay?: number;
  as?: "div" | "section" | "li" | "span";
}) {
  const ref = useRef<HTMLElement | null>(null);

  // Класс скрытия и задержку ставим до отрисовки, чтобы не было вспышки контента.
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (window.matchMedia(REDUCED_MOTION_QUERY).matches) return;
    el.classList.add("lp-reveal");
    el.style.transitionDelay = `${delay}ms`;
  }, [delay]);

  // Появление по вхождению во вьюпорт — класс переключаем прямо на элементе.
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (window.matchMedia(REDUCED_MOTION_QUERY).matches) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          el.classList.add("lp-visible");
          observer.disconnect();
        }
      },
      { threshold: 0.15, rootMargin: "0px 0px -8% 0px" }
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  return (
    <Tag
      // @ts-expect-error — общий ref для допустимых тегов
      ref={ref}
      className={cn("min-w-0", className)}
    >
      {children}
    </Tag>
  );
}
