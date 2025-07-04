# Build stage
FROM node:20-alpine AS builder

WORKDIR /app

# Install pnpm
RUN corepack enable && corepack prepare pnpm@latest --activate

# Copy workspace configuration
COPY package.json pnpm-workspace.yaml ./
COPY pnpm-lock.yaml ./

# Copy package manifests
COPY packages/domain/package.json packages/domain/
COPY apps/api/package.json apps/api/

# Install dependencies
RUN pnpm install --frozen-lockfile

# Copy source code
COPY packages/domain packages/domain
COPY apps/api apps/api

# Build packages
RUN pnpm --filter @sekiban/dapr-sample-domain build
RUN pnpm --filter @sekiban/dapr-sample-api build

# Runtime stage
FROM node:20-alpine

WORKDIR /app

# Install pnpm
RUN corepack enable && corepack prepare pnpm@latest --activate

# Copy built application
COPY --from=builder /app/package.json /app/pnpm-workspace.yaml ./
COPY --from=builder /app/pnpm-lock.yaml ./
COPY --from=builder /app/packages/domain/package.json packages/domain/
COPY --from=builder /app/packages/domain/dist packages/domain/dist
COPY --from=builder /app/apps/api/package.json apps/api/
COPY --from=builder /app/apps/api/dist apps/api/dist

# Install production dependencies only
RUN pnpm install --prod --frozen-lockfile

# Expose port
EXPOSE 3000

# Start the application
WORKDIR /app/apps/api
CMD ["node", "dist/server.js"]