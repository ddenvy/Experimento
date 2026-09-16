import type { Metadata } from "next";
import { Manrope } from "next/font/google";
import "./globals.css";
import { Sidebar } from "@/components/layout/Sidebar";
import { AuthGuard } from "@/components/AuthGuard";

const manrope = Manrope({
  subsets: ["latin"],
  variable: "--font-manrope",
});

export const metadata: Metadata = {
  title: "Experimento — AI Formulation & R&D Co-Pilot",
  description: "AI-driven formulation design, property prediction, and simulation with full audit traceability.",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className={manrope.variable}>
      <body className="min-h-screen bg-background font-sans antialiased">
        <AuthGuard>
          <div className="flex min-h-screen">
            <Sidebar />
            <main className="flex-1 p-6 md:p-8">{children}</main>
          </div>
        </AuthGuard>
      </body>
    </html>
  );
}
