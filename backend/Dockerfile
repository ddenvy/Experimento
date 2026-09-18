# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy all source projects and restore (cache layer)
COPY src/ ./src/
RUN dotnet restore src/Experimento.WebApi/Experimento.WebApi.csproj

# Build
RUN dotnet build src/Experimento.WebApi/Experimento.WebApi.csproj -c Release --no-restore

# Publish the WebApi project
RUN dotnet publish src/Experimento.WebApi/Experimento.WebApi.csproj \
    -c Release --no-build -o /app/publish

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user + native deps for gRPC/GSSAPI (required by Gemini connector)
RUN apt-get update && apt-get install -y --no-install-recommends adduser libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && addgroup --system appgroup && adduser --system --ingroup appgroup appuser
COPY --from=build --chown=appuser:appgroup /app/publish ./

USER appuser

# Окружение НЕ задаётся в образе: по умолчанию ASP.NET Core стартует как Production,
# а Development (dev-ключ JWT, Scalar, тестовый админ) включается только явно при запуске.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Experimento.WebApi.dll"]
