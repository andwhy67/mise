# syntax=docker/dockerfile:1
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
ARG VITE_API_URL=
ENV VITE_API_URL=$VITE_API_URL
RUN npm run build

FROM nginx:1.27-alpine AS runtime
# Шаблон: при старте nginx подставит PORT и API_UPSTREAM (остальные $-переменные nginx не трогаются — их нет в окружении).
COPY nginx.conf /etc/nginx/templates/default.conf.template
ENV PORT=80     API_UPSTREAM=http://api:8080
COPY security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=15s --timeout=3s --retries=5 CMD wget -qO- "http://127.0.0.1:${PORT}/" >/dev/null || exit 1
