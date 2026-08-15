# ==============================================================================
# SAP B1 通用审批平台 - 世界级多阶段生产 Dockerfile
# 支持多架构 (Linux/Windows, amd64 / arm64) 构建与最小化安全运行时
# ==============================================================================

# ------------------------------------------------------------------------------
# 阶段 1: 前端 Vue 3 + TypeScript 静态构建
# ------------------------------------------------------------------------------
FROM node:24-alpine AS web-builder
WORKDIR /app/web

COPY src/Approval.Web/package*.json ./
RUN npm ci

COPY src/Approval.Web/ ./
RUN npm run build

# ------------------------------------------------------------------------------
# 阶段 2: 后端 .NET 8 编译与发布
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dotnet-builder
WORKDIR /app

COPY SAP_B1_ApprovalCenter.sln ./
COPY src/Approval.Domain/Approval.Domain.csproj src/Approval.Domain/
COPY src/Approval.Application/Approval.Application.csproj src/Approval.Application/
COPY src/Approval.Infrastructure/Approval.Infrastructure.csproj src/Approval.Infrastructure/
COPY src/Approval.SapAdapter/Approval.SapAdapter.csproj src/Approval.SapAdapter/
COPY src/Approval.Api/Approval.Api.csproj src/Approval.Api/
COPY src/Approval.Worker/Approval.Worker.csproj src/Approval.Worker/
COPY tests/Approval.Domain.Tests/Approval.Domain.Tests.csproj tests/Approval.Domain.Tests/
COPY tests/Approval.Api.IntegrationTests/Approval.Api.IntegrationTests.csproj tests/Approval.Api.IntegrationTests/

RUN dotnet restore SAP_B1_ApprovalCenter.sln

COPY . .

# 执行自动化测试门禁
RUN dotnet test tests/Approval.Domain.Tests/Approval.Domain.Tests.csproj -c Release --no-restore

# 发布 API 服务
RUN dotnet publish src/Approval.Api/Approval.Api.csproj -c Release -o /app/publish/api --no-restore

# 发布 Worker 服务
RUN dotnet publish src/Approval.Worker/Approval.Worker.csproj -c Release -o /app/publish/worker --no-restore

# 将前端静态产物拷贝至 API wwwroot
COPY --from=web-builder /app/web/dist /app/publish/api/wwwroot

# ------------------------------------------------------------------------------
# 阶段 3: API 生产运行时 (最小化与安全加固)
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS api-runtime
WORKDIR /app
EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# 使用非 root 用户运行保障安全
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=dotnet-builder --chown=appuser:appgroup /app/publish/api .

HEALTHCHECK --interval=15s --timeout=3s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "Approval.Api.dll"]

# ------------------------------------------------------------------------------
# 阶段 4: Worker 生产运行时
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS worker-runtime
WORKDIR /app

ENV DOTNET_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=dotnet-builder --chown=appuser:appgroup /app/publish/worker .

ENTRYPOINT ["dotnet", "Approval.Worker.dll"]
