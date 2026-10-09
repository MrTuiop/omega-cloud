# Multi-stage Dockerfile for Omega Cloud Angular Frontend
# Stage 1: Build Angular SPA
FROM node:22-alpine AS build
WORKDIR /app

# Install dependencies using clean install
COPY package*.json ./
RUN npm ci

# Copy source and build production bundle
COPY . .
RUN npm run build

# Stage 2: Serve static files via volume sync & embedded httpd
FROM alpine:3.20 AS runtime
WORKDIR /srv/frontend

# Install busybox-extras to provide httpd applet (omitted in minimal alpine busybox)
RUN apk add --no-cache busybox-extras

# Copy compiled assets from build stage
COPY --from=build /app/dist/frontend/browser/ /srv/frontend-build/

# Ensure output directory exists, sync build into volume, and start lightweight file server
EXPOSE 80

HEALTHCHECK --interval=5s --timeout=3s --retries=5 \
  CMD wget -qO- http://127.0.0.1:80/index.html > /dev/null || exit 1

CMD ["sh", "-c", "mkdir -p /srv/frontend && cp -a /srv/frontend-build/. /srv/frontend/ && exec busybox-extras httpd -f -p 80 -h /srv/frontend"]

