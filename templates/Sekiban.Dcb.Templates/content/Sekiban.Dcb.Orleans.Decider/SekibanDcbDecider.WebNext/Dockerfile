# syntax=docker/dockerfile:1

# ============================================
# Build Stage
# ============================================
FROM node:22-alpine AS builder

WORKDIR /app

# Install the exact dependency versions from package-lock.json. The lockfile is created by the first
# `npm install` (the AppHost runs it when WebNext starts); commit it before building this image.
COPY SekibanDcbDecider.WebNext/package*.json ./
RUN npm ci --ignore-scripts

# Copy source and build
COPY SekibanDcbDecider.WebNext/ ./

# Build the Next.js app (standalone output)
RUN npm run build

# Ensure public folder exists (may not exist in all projects)
RUN mkdir -p /app/public

# ============================================
# Production Stage
# ============================================
FROM node:22-alpine AS runner

WORKDIR /app

ENV NODE_ENV=production
ENV PORT=3000
ENV HOSTNAME="0.0.0.0"

# Create non-root user
RUN addgroup --system --gid 1001 nodejs && \
    adduser --system --uid 1001 nextjs

# Copy built assets
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=builder --chown=nextjs:nodejs /app/public ./public

USER nextjs

EXPOSE 3000

CMD ["node", "server.js"]
