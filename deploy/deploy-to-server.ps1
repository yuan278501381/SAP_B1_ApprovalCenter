<#
.SYNOPSIS
    SAP B1 10.0 通用审批平台全自动部署与运维脚本 (Windows / Windows Server 2022/2025)
.DESCRIPTION
    一键完成：编译打包 -> ApprovalDB 独立库建库与 DDL 导入 -> Windows 服务注册与开机自启 -> 防火墙端口放行
.PARAMETER TargetServer
    目标服务器 IP 或主机名 (默认 127.0.0.1 本地部署，也可指定 192.168.134.9 远程测试机)
.PARAMETER SqlServerInstance
    SQL Server 实例地址 (默认 127.0.0.1 或 192.168.134.9)
.PARAMETER SqlSaPassword
    SA 密码 (默认 123456@a)
.PARAMETER ServicePort
    Web API 与前端工作台服务端口 (默认 5000)
#>
param(
    [string]$TargetServer = "127.0.0.1",
    [string]$SqlServerInstance = "127.0.0.1",
    [string]$SqlSaPassword = "123456@a",
    [int]$ServicePort = 5000
)

$ErrorActionPreference = "Stop"
$rootDir = (Get-Item $PSScriptRoot).Parent.FullName
$distDir = Join-Path $rootDir "dist\win-x64"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  启动 SAP B1 通用审批平台自动化部署流程" -ForegroundColor Cyan
Write-Host "  目标服务器: $TargetServer | 服务端口: $ServicePort" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. 编译发布
Write-Host "`n[1/4] 执行一键构建打包..." -ForegroundColor Yellow
& "$PSScriptRoot\build.ps1" -Architecture win-x64 -Configuration Release

# 2. 初始化 ApprovalDB 数据库
Write-Host "`n[2/4] 检查并初始化 ApprovalDB 数据库..." -ForegroundColor Yellow
$initSql = Join-Path $rootDir "database\01_init_approval_db.sql"
$createDbSql = @"
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'ApprovalDB')
BEGIN
    CREATE DATABASE ApprovalDB COLLATE Chinese_PRC_CI_AS;
END
"@

# 使用 sqlcmd 或 ADO.NET 执行初始化
try {
    $connStr = "Server=$SqlServerInstance;Database=master;User Id=sa;Password=$SqlSaPassword;TrustServerCertificate=True;"
    $conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $createDbSql
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()
    Write-Host "  ApprovalDB 数据库已确认存在。" -ForegroundColor Green

    # 执行 GBK 结构初始化
    $apprConnStr = "Server=$SqlServerInstance;Database=ApprovalDB;User Id=sa;Password=$SqlSaPassword;TrustServerCertificate=True;"
    $gbkEncoding = [System.Text.Encoding]::GetEncoding("GBK")
    $ddlContent = [System.IO.File]::ReadAllText($initSql, $gbkEncoding)

    $conn = New-Object System.Data.SqlClient.SqlConnection($apprConnStr)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $ddlContent
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()
    Write-Host "  ApprovalDB 核心表结构与并发索引已初始化完成。" -ForegroundColor Green
} catch {
    Write-Warning "  自动执行 SQL 遇到警告或已初始化: $($_.Exception.Message)"
}

# 3. 注册并配置 Windows 服务
Write-Host "`n[3/4] 注册后台守护进程 Windows 服务..." -ForegroundColor Yellow
$apiExe = Join-Path $distDir "Approval.Api\Approval.Api.exe"
$workerExe = Join-Path $distDir "Approval.Worker\Approval.Worker.exe"

# 停止旧服务
Stop-Service -Name "ApprovalPlatformApi" -ErrorAction SilentlyContinue
Stop-Service -Name "ApprovalPlatformWorker" -ErrorAction SilentlyContinue

# 使用 sc.exe 创建/更新服务
if (!(Get-Service -Name "ApprovalPlatformApi" -ErrorAction SilentlyContinue)) {
    sc.exe create "ApprovalPlatformApi" binPath= "`"$apiExe`" --urls=http://0.0.0.0:$ServicePort" start= auto DisplayName= "SAP B1 Approval Platform API"
    Write-Host "  已注册 Windows 服务: ApprovalPlatformApi (端口: $ServicePort)" -ForegroundColor Green
}

if (!(Get-Service -Name "ApprovalPlatformWorker" -ErrorAction SilentlyContinue)) {
    sc.exe create "ApprovalPlatformWorker" binPath= "`"$workerExe`"" start= auto DisplayName= "SAP B1 Approval Platform Worker"
    Write-Host "  已注册 Windows 服务: ApprovalPlatformWorker (Outbox守护中继)" -ForegroundColor Green
}

# 启动服务
Start-Service -Name "ApprovalPlatformApi" -ErrorAction SilentlyContinue
Start-Service -Name "ApprovalPlatformWorker" -ErrorAction SilentlyContinue

# 4. 防火墙端口放行
Write-Host "`n[4/4] 检查并放行防火墙端口 $ServicePort..." -ForegroundColor Yellow
try {
    if (!(Get-NetFirewallRule -DisplayName "SAP B1 Approval Platform" -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName "SAP B1 Approval Platform" -Direction Inbound -LocalPort $ServicePort -Protocol TCP -Action Allow | Out-Null
        Write-Host "  已成功添加防火墙入站允许规则 (TCP $ServicePort)" -ForegroundColor Green
    }
} catch {
    Write-Host "  防火墙规则配置已跳过或需管理员权限" -ForegroundColor DarkGray
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "  🎉 部署完成！服务运行状态:" -ForegroundColor Green
Write-Host "  - 审批中心 Web 门户: http://$TargetServer`:$ServicePort" -ForegroundColor Green
Write-Host "  - Swagger API 文档:  http://$TargetServer`:$ServicePort/swagger" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
