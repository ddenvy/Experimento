# ---- Build stage ----
FROM node:22-alpine AS build
WORKDIR /app

# NEXT_PUBLIC_* vars are inlined at build time (not runtime),
# so they must be provided during docker build via build args.
ARG NEXT_PUBLIC_API_URL
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL

# Install dependencies (cache layer)
COPY package.json package-lock.json* ./
RUN npm ci

# Build the Next.js app (output: "standalone" produces a self-contained minimal server)
COPY . .
RUN npm run build

# ---- Runtime stage ----
FROM node:22-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV HOSTNAME=0.0.0.0
ENV PORT=3000

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Берём только standalone-рантайм: без devDependencies и полного node_modules
# (раньше сюда копировался весь node_modules из build-стадии).
COPY --from=build --chown=appuser:appgroup /app/.next/standalone ./
COPY --from=build --chown=appuser:appgroup /app/.next/static ./.next/static

USER appuser

EXPOSE 3000
CMD ["node", "server.js"]
