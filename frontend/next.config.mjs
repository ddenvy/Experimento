/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  // Минимальный самодостаточный сервер для Docker (.next/standalone), без dev-зависимостей.
  output: "standalone",
};

export default nextConfig;
