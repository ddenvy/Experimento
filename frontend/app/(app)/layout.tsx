import { AuthGuard } from "@/components/AuthGuard";
import { Sidebar } from "@/components/layout/Sidebar";

export default function AppLayout({ children }: { children: React.ReactNode }) {
  return (
    <AuthGuard>
      <div className="flex min-h-screen">
        <Sidebar />
        <main className="flex-1 p-6 md:p-8">{children}</main>
      </div>
    </AuthGuard>
  );
}
