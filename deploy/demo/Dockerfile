# The public demo in one container, for a single free web service (Render) next to a hosted PostgreSQL (Neon): nginx
# with the web app in front, the API, the stand-in payment provider and Mailpit, each on a local port.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props Gise.slnx ./
COPY src/ src/
RUN dotnet publish src/Gise.Api/Gise.Api.csproj -c Release -o /app/api /p:UseAppHost=false \
 && dotnet publish src/Gise.PaymentSandbox/Gise.PaymentSandbox.csproj -c Release -o /app/payments /p:UseAppHost=false

FROM axllent/mailpit:v1.31.6 AS mail

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends nginx \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/api ./api
COPY --from=build /app/payments ./payments
COPY --from=web /web/dist ./web
COPY --from=mail /mailpit /usr/local/bin/mailpit
COPY deploy/demo/nginx.conf ./nginx.conf
COPY deploy/demo/start.sh ./start.sh
USER $APP_UID
ENTRYPOINT ["sh", "/app/start.sh"]
