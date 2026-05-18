# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
# ======================================
# Base runtime image
# ======================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER root
WORKDIR /app

EXPOSE 8080
EXPOSE 8081

# Ensure the app listens on port 8080 inside the container
ENV ASPNETCORE_URLS=http://+:8080

# Optional: install procps/curl only if needed for debugging or diagnostics
RUN apt-get update && apt-get install -y \
    curl \
    procps \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Switch back to non-root if APP_UID is provided by Visual Studio / Docker profile
ARG APP_UID=64198
USER $APP_UID


# ======================================
# Build stage
# ======================================

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["TraceabilityDriver/TraceabilityDriver.csproj", "TraceabilityDriver/"]
RUN dotnet restore "./TraceabilityDriver/TraceabilityDriver.csproj"

COPY . .
WORKDIR "/src/TraceabilityDriver"
RUN dotnet build "./TraceabilityDriver.csproj" -c $BUILD_CONFIGURATION -o /app/build


# ======================================
# Publish stage
# ======================================

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TraceabilityDriver.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ======================================
# Final runtime image
# ======================================

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TraceabilityDriver.dll"]