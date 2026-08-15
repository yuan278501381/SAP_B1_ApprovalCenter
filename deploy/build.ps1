<#
.SYNOPSIS
    SAP B1 10.0 通用可靠审批平台一键跨架构构建与发布脚本
.DESCRIPTION
    支持 Windows x64 与 ARM64 双架构编译打包，集成前端 Vite 构建与后端 Single-File 发布
.PARAMETER Architecture
    目标架构: win-x64 或 win-arm64 (默认 win-x64)
.PARAMETER Configuration
    编译配置: Release 或 Debug (默认 Release)
#>
param(
    [ValidateSet("win-x64", "win-arm64", "linux-x64")]
    [string]$Architecture = "win-x64",

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$rootDir = (Get-Item $PSScriptRoot).Parent.FullName
$distDir = Join-Path $rootDir "dist\$Architecture"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  启动 SAP B1 通用审批平台自动化发布流程" -ForegroundColor Cyan
Write-Host "  目标架构: $Architecture | 编译模式: $Configuration" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. 运行自动化测试门禁
Write-Host "`n[1/4] 执行自动化测试套件与质量门禁..." -ForegroundColor Yellow
dotnet test "$rootDir\SAP_B1_ApprovalCenter.sln" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "自动化测试失败，中止构建流程！"
}

# 2. 构建前端单页应用
Write-Host "`n[2/4] 构建前端 Vue 3 + TypeScript 静态产物..." -ForegroundColor Yellow
$webDir = Join-Path $rootDir "src\Approval.Web"
Push-Location $webDir
npm run build
Pop-Location

# 3. 发布后端 API 服务 (跨架构自包含独立发布)
Write-Host "`n[3/4] 发布后端 API 服务 ($Architecture)..." -ForegroundColor Yellow
$apiProj = Join-Path $rootDir "src\Approval.Api\Approval.Api.csproj"
$apiOut = Join-Path $distDir "Approval.Api"
dotnet publish $apiProj -c $Configuration -r $Architecture --self-contained true -o $apiOut

# 4. 发布后台 Worker 守护进程
Write-Host "`n[4/4] 发布后台 Worker 守护进程 ($Architecture)..." -ForegroundColor Yellow
$workerProj = Join-Path $rootDir "src\Approval.Worker\Approval.Worker.csproj"
$workerOut = Join-Path $distDir "Approval.Worker"
dotnet publish $workerProj -c $Configuration -r $Architecture --self-contained true -o $workerOut

# 拷贝前端产物至 API wwwroot 并执行完整性门禁
$wwwroot = Join-Path $apiOut "wwwroot"
if (Test-Path "$webDir\dist") {
    Write-Host "  执行前端静态资产完整性门禁校验..." -ForegroundColor DarkGray
    $indexHtmlPath = Join-Path "$webDir\dist" "index.html"
    if (-not (Test-Path $indexHtmlPath)) {
        Write-Error "前端构建产物缺失 index.html，中止构建！"
    }
    
    # 递归镜像拷贝至 API wwwroot
    New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
    Copy-Item -Path "$webDir\dist\*" -Destination $wwwroot -Recurse -Force
    
    # 校验 assets 目录与关键入口文件
    $targetIndexHtml = Join-Path $wwwroot "index.html"
    $targetAssets = Join-Path $wwwroot "assets"
    if (-not (Test-Path $targetIndexHtml) -or -not (Test-Path $targetAssets)) {
        Write-Error "API wwwroot 部署包资产不完整，中止构建！"
    }
    
    $assetCount = (Get-ChildItem $targetAssets).Count
    Write-Host "  ✅ 前端静态资产完整性验证通过: 已同步 $assetCount 个静态资源至 API wwwroot。" -ForegroundColor Green
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "  恭喜！跨架构构建完成，发布产物目录:" -ForegroundColor Green
Write-Host "  $distDir" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
