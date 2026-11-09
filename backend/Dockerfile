# syntax=docker/dockerfile:1
# Build context: repository root (needs docs/CHANGELOG.md for the "system version" screen).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/BlueMeal.sln ./
COPY backend/src/BlueMeal.Domain/BlueMeal.Domain.csproj src/BlueMeal.Domain/
COPY backend/src/BlueMeal.Application/BlueMeal.Application.csproj src/BlueMeal.Application/
COPY backend/src/BlueMeal.Infrastructure/BlueMeal.Infrastructure.csproj src/BlueMeal.Infrastructure/
COPY backend/src/BlueMeal.Api/BlueMeal.Api.csproj src/BlueMeal.Api/
RUN dotnet restore src/BlueMeal.Api/BlueMeal.Api.csproj
COPY backend/src ./src
COPY docs/CHANGELOG.md /docs/CHANGELOG.md
ARG BUILD_VERSION=1.0.0
ARG GIT_COMMIT=unknown
RUN dotnet publish src/BlueMeal.Api/BlueMeal.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false \
    /p:Version=${BUILD_VERSION} /p:SourceRevisionId=${GIT_COMMIT}

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
# icu: vi-VN culture; tzdata: Asia/Ho_Chi_Minh; fonts: ClosedXML column sizing
RUN apk add --no-cache icu-libs tzdata fontconfig ttf-dejavu \
 && mkdir -p /data/files /data/exports /app/logs \
 && chown -R app:app /data /app/logs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Asia/Ho_Chi_Minh \
    DOTNET_gcServer=1
WORKDIR /app
COPY --from=build --chown=app:app /app .
ARG BUILD_VERSION=1.0.0
ARG GIT_COMMIT=unknown
ARG BUILD_TIME=unknown
ENV BLUEMEAL_BUILD_VERSION=${BUILD_VERSION} BLUEMEAL_GIT_COMMIT=${GIT_COMMIT} BLUEMEAL_BUILD_TIME=${BUILD_TIME}
USER app
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=5 CMD wget -qO- http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "BlueMeal.Api.dll"]
