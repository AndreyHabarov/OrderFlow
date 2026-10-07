# syntax=docker/dockerfile:1

# One Dockerfile for every .NET service. Pick the project with --build-arg PROJECT=OrderFlow.Api | OrderFlow.Inventory | OrderFlow.Payments
ARG PROJECT=OrderFlow.Api

# ---- restore: copy only project files first so the layer is cached until dependencies change ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS restore
ARG PROJECT
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/OrderFlow.Api/OrderFlow.Api.csproj src/OrderFlow.Api/
COPY src/OrderFlow.Application/OrderFlow.Application.csproj src/OrderFlow.Application/
COPY src/OrderFlow.Domain/OrderFlow.Domain.csproj src/OrderFlow.Domain/
COPY src/OrderFlow.Infrastructure/OrderFlow.Infrastructure.csproj src/OrderFlow.Infrastructure/
COPY src/OrderFlow.Contracts/OrderFlow.Contracts.csproj src/OrderFlow.Contracts/
COPY src/OrderFlow.Messaging/OrderFlow.Messaging.csproj src/OrderFlow.Messaging/
COPY src/OrderFlow.Inventory/OrderFlow.Inventory.csproj src/OrderFlow.Inventory/
COPY src/OrderFlow.Payments/OrderFlow.Payments.csproj src/OrderFlow.Payments/
RUN dotnet restore src/${PROJECT}/${PROJECT}.csproj

# ---- publish ----
FROM restore AS publish
ARG PROJECT
COPY src/ src/
RUN dotnet publish src/${PROJECT}/${PROJECT}.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- runtime: only the ASP.NET runtime, no SDK, non-root user ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
ARG PROJECT
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV APP_DLL=${PROJECT}.dll
EXPOSE 8080
COPY --from=publish /app/publish .
USER $APP_UID
# Shell form of exec: lets the DLL name come from the build argument and still makes dotnet PID 1 (receives SIGTERM).
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
