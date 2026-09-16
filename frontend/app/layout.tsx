import type { Metadata } from "next";
import { Manrope } from "next/font/google";
import "./globals.css";

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
      <body className="min-h-screen bg-background font-sans antialiased">{children}</body>
    </html>
  );
}
