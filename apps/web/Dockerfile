FROM node:24-alpine AS dependencies
WORKDIR /workspace
COPY package.json package-lock.json ./
COPY apps/web/package.json apps/web/package.json
COPY packages/contracts/package.json packages/contracts/package.json
COPY packages/taxonomy/package.json packages/taxonomy/package.json
RUN npm ci

FROM dependencies AS build
COPY . .
RUN npm run build:web

FROM node:24-alpine AS runtime
ENV NODE_ENV=production
WORKDIR /app
RUN rm -rf /usr/local/lib/node_modules/npm /usr/local/lib/node_modules/corepack /opt/yarn-v1.22.22 \
    && rm -f /usr/local/bin/npm /usr/local/bin/npx /usr/local/bin/corepack /usr/local/bin/yarn /usr/local/bin/yarnpkg \
    && addgroup --system --gid 1001 nodejs \
    && adduser --system --uid 1001 nextjs
COPY --from=build --chown=nextjs:nodejs /workspace/apps/web/.next/standalone ./
COPY --from=build --chown=nextjs:nodejs /workspace/apps/web/.next/static ./apps/web/.next/static
USER nextjs
EXPOSE 3000
ENV PORT=3000 HOSTNAME=0.0.0.0
CMD ["node", "apps/web/server.js"]
