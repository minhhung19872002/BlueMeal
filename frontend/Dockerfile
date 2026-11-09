# syntax=docker/dockerfile:1
# Build context: repository root.
FROM node:22-alpine AS build
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
ARG BUILD_VERSION=1.0.0
ARG GIT_COMMIT=unknown
ENV VITE_APP_VERSION=${BUILD_VERSION} VITE_GIT_COMMIT=${GIT_COMMIT}
RUN npm run build

# nginx + ModSecurity v3 + OWASP CRS 4 LTS (runs as non-root on 8080/8443)
FROM owasp/modsecurity-crs:4.25.2-nginx-202610180210-lts
COPY --from=build /app/dist /usr/share/nginx/html
COPY deploy/nginx/default.conf.template /etc/nginx/templates/conf.d/default.conf.template
COPY deploy/nginx/app.conf /etc/nginx/bluemeal/app.conf
COPY deploy/nginx/modsecurity/ /opt/modsecurity/rules/before-crs/
ENV BM_API_UPSTREAM=api:8080 \
    BM_EDGE_RATE=50r/s \
    ALLOWED_METHODS="GET HEAD POST PUT PATCH DELETE OPTIONS" \
    BLOCKING_PARANOIA=1 \
    ANOMALY_INBOUND=5 \
    ANOMALY_OUTBOUND=4 \
    SERVER_NAME=_ \
    NGINX_ALWAYS_TLS_REDIRECT=off \
    MODSEC_RULE_ENGINE=On
