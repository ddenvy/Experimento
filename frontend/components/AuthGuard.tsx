"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { refreshAccessToken } from "@/lib/api";

export function AuthGuard({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  // Пока проверяем сессию через refresh-cookie, интерфейс не показываем.
  const [ready, setReady] = useState(false);

  useEffect(() => {
    let cancelled = false;

    async function bootstrap(): Promise<void> {
      // Access-токен живёт в памяти и теряется при перезагрузке — восстанавливаем сессию
      // через refresh-токен в httpOnly-cookie.
      const authenticated = await refreshAccessToken();
      if (cancelled) return;
      if (!authenticated) {
        router.push("/login");
        return;
      }
      setReady(true);
    }

    void bootstrap();
    return () => {
      cancelled = true;
    };
  }, [pathname, router]);

  if (!ready) {
    return (
      <div className="flex items-center justify-center min-h-screen text-sm text-muted-foreground">
        Loading…
      </div>
    );
  }

  return <>{children}</>;
}
