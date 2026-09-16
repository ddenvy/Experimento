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

# Build the Next.js app
COPY . .
RUN npm run build

# ---- Runtime stage ----
FROM node:22-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

COPY --from=build /app/package.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/.next ./.next

USER appuser

EXPOSE 3000
CMD ["npm", "start"]
