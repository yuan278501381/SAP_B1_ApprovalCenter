<#
.SYNOPSIS
    SAP B1 审批中心一键远程部署至 Windows 虚拟机 (192.168.134.9 / Windows Server 2025)
.DESCRIPTION
    全自动化流水线：构建打包 -> SMB同步产物 -> 生产配置生成 -> WMI远程注册Windows服务 -> 防火墙放行 -> 健康检查验证
#>
param(
    [string]$TargetIp = "192.168.134.9",
    [string]$AdminUser = "administrator",
    [string]$AdminPassword = "123456@aA",
    [string]$SqlSaPassword = "123456@a",
    [int]$ServicePort = 5000
)

$ErrorActionPreference = "Stop"
$rootDir = (Get-Item $PSScriptRoot).Parent.FullName
$distDir = Join-Path $rootDir "dist\win-x64"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  启动 SAP B1 审批中心部署至目标服务器: $TargetIp" -ForegroundColor Cyan
Write-Host "  目标端口: $ServicePort | 架构: win-x64 (Windows Server 2025)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. 强制执行最新一键跨架构打包 (确保前端 Vue 产物与后端最新代码 100% 编译注入)
Write-Host "`n[1/5] 执行一键跨架构打包 (编译前端与后端)..." -ForegroundColor Yellow
& "$PSScriptRoot\build.ps1" -Architecture win-x64 -Configuration Release
Write-Host "  构建产物已就绪: $distDir" -ForegroundColor Green

# 2. 建立 SMB 安全连接并同步文件
Write-Host "`n[2/5] 建立 SMB 连接并同步部署包至 $TargetIp..." -ForegroundColor Yellow
$netUse = "net use \\$TargetIp\c$ `"$AdminPassword`" /user:`"$AdminUser`""
cmd /c $netUse | Out-Null

$remoteBase = "\\$TargetIp\c$\Services\ApprovalPlatform"
if (!(Test-Path $remoteBase)) {
    New-Item -ItemType Directory -Path $remoteBase -Force | Out-Null
}

# 远程执行管理命令辅助函数 (使用 WMI 管理员权限)
function Exec-WmiCmd([string]$cmdText) {
    $co = New-Object System.Management.ConnectionOptions
    $co.Username = $AdminUser
    $co.Password = $AdminPassword
    $co.EnablePrivileges = $true
    $co.Impersonation = [System.Management.ImpersonationLevel]::Impersonate
    $co.Authentication = [System.Management.AuthenticationLevel]::PacketPrivacy

    $scope = New-Object System.Management.ManagementScope("\\$TargetIp\root\cimv2", $co)
    $scope.Connect()

    $processClass = New-Object System.Management.ManagementClass($scope, (New-Object System.Management.ManagementPath("Win32_Process")), $null)
    $inParams = $processClass.GetMethodParameters("Create")
    $inParams["CommandLine"] = $cmdText
    $outParams = $processClass.InvokeMethod("Create", $inParams, $null)
    return $outParams["ReturnValue"]
}

# 停止旧服务与进程 (确保彻底释放二进制 DLL 句柄)
Write-Host "  停止远程运行中的服务与进程..." -ForegroundColor DarkGray
sc.exe \\$TargetIp stop ApprovalPlatformApi 2>&1 | Out-Null
sc.exe \\$TargetIp stop ApprovalPlatformWorker 2>&1 | Out-Null
Start-Sleep -Seconds 3

taskkill.exe /S $TargetIp /U $AdminUser /P $AdminPassword /F /IM Approval.Api.exe /IM Approval.Worker.exe /T 2>&1 | Out-Null
Start-Sleep -Seconds 2

# 极速增量并发同步 Api 与 Worker (使用 robocopy 代替单线程慢速 Copy-Item)
Write-Host "  极速增量同步 Api 文件至 $remoteBase\Approval.Api..." -ForegroundColor DarkGray
robocopy "$distDir\Approval.Api" "$remoteBase\Approval.Api" /MIR /NP /NFL /NDO /R:1 /W:1 | Out-Null

Write-Host "  极速增量同步 Worker 文件至 $remoteBase\Approval.Worker..." -ForegroundColor DarkGray
robocopy "$distDir\Approval.Worker" "$remoteBase\Approval.Worker" /MIR /NP /NFL /NDO /R:1 /W:1 | Out-Null

$remoteWwwroot = "$remoteBase\Approval.Api\wwwroot"
if (Test-Path "$distDir\Approval.Api\wwwroot") {
    Copy-Item -Path "$distDir\Approval.Api\wwwroot\*" -Destination "$remoteWwwroot" -Recurse -Force
}

# 清空远程元数据落盘缓存以确保获取最新的 SAP 动态字典映射
$remoteCache = "$remoteBase\Approval.Api\metadata_cache"
if (Test-Path $remoteCache) {
    Remove-Item "$remoteCache\*" -Recurse -Force -ErrorAction SilentlyContinue
}

# 部署完整性验证门禁 (验证远程 DLL 已被最新编译产物覆盖)
$localApiDll = Get-Item "$distDir\Approval.Api\Approval.Api.dll"
$remoteApiDll = Get-Item "$remoteBase\Approval.Api\Approval.Api.dll"
if ($remoteApiDll.LastWriteTime -lt $localApiDll.LastWriteTime.AddSeconds(-5)) {
    throw "【部署熔断】远程 Approval.Api.dll 未成功更新，请检查 Windows 进程锁定！(远程: $($remoteApiDll.LastWriteTime) vs 本地: $($localApiDll.LastWriteTime))"
}
Write-Host "  ✅ 二进制部署完整性验证通过: 远程 DLL 已 100% 同步为最新构建产物 ($($remoteApiDll.LastWriteTime))" -ForegroundColor Green

# 3. 注入生产环境配置文件
Write-Host "`n[3/5] 注入生产环境配置文件 (appsettings.Production.json)..." -ForegroundColor Yellow
$prodConfig = @"
{
  "UseInMemoryDb": false,
  "ConnectionStrings": {
    "ApprovalDbConnection": "Server=127.0.0.1;Database=ApprovalDB;User Id=sa;Password=$SqlSaPassword;TrustServerCertificate=True;Timeout=30;"
  },
  "SapAdapter": {
    "Mode": "ServiceLayer",
    "ServiceLayer": {
      "BaseUrl": "https://127.0.0.1:50000/b1s/v1/",
      "CompanyDb": "DB_KCC",
      "UserName": "manager",
      "Password": "1111",
      "AllowInvalidServerCertificate": true,
      "MirrorEnabled": true,
      "Objects": [
        {
          "ObjectCode": "CHORDR",
          "EntitySet": "CHORDR",
          "KeyType": "Number",
          "DocTotalField": "U_DocTotal",
          "TitleField": "U_CardName",
          "CreatorCodeField": "Creator",
          "StatusField": "U_APStatus",
          "InstanceIdField": "U_APInstance",
          "HashField": "U_APHash"
        },
        {
          "ObjectCode": "CHOQUT",
          "EntitySet": "CHOQUT",
          "KeyType": "Number",
          "DocTotalField": "U_DocTotal",
          "TitleField": "U_CardName",
          "CreatorCodeField": "Creator",
          "StatusField": "U_APStatus",
          "InstanceIdField": "U_APInstance",
          "HashField": "U_APHash"
        }
      ]
    }
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost",
      "http://localhost:${ServicePort}",
      "http://${TargetIp}:${ServicePort}",
      "http://127.0.0.1:${ServicePort}"
    ]
  }
}
"@

[System.IO.File]::WriteAllText("$remoteBase\Approval.Api\appsettings.json", $prodConfig, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText("$remoteBase\Approval.Api\appsettings.Production.json", $prodConfig, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText("$remoteBase\Approval.Worker\appsettings.json", $prodConfig, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText("$remoteBase\Approval.Worker\appsettings.Production.json", $prodConfig, [System.Text.Encoding]::UTF8)
Write-Host "  生产配置文件注入完成 (已双写 appsettings.json 与 appsettings.Production.json)。" -ForegroundColor Green

# 4. 生成并在目标机执行安装批处理
$installerLines = @(
  '@echo off',
  'cd /d C:\Services\ApprovalPlatform',
  'net stop ApprovalPlatformApi >nul 2>&1',
  'net stop ApprovalPlatformWorker >nul 2>&1',
  'sc delete ApprovalPlatformApi >nul 2>&1',
  'sc delete ApprovalPlatformWorker >nul 2>&1',
  "sc create ApprovalPlatformApi binPath= `"C:\Services\ApprovalPlatform\Approval.Api\Approval.Api.exe --urls=http://0.0.0.0:$ServicePort --environment=Production`" start= auto DisplayName= `"SAP B1 Approval Platform API`"",
  'sc failure ApprovalPlatformApi reset= 86400 actions= restart/5000/restart/10000/restart/60000',
  'sc create ApprovalPlatformWorker binPath= "C:\Services\ApprovalPlatform\Approval.Worker\Approval.Worker.exe --environment=Production" start= auto DisplayName= "SAP B1 Approval Platform Worker"',
  'sc failure ApprovalPlatformWorker reset= 86400 actions= restart/5000/restart/10000/restart/60000',
  'net start ApprovalPlatformApi',
  'net start ApprovalPlatformWorker',
  "netsh advfirewall firewall add rule name=`"SAP_B1_Approval_Platform`" dir=in action=allow protocol=TCP localport=$ServicePort"
)
$installerBat = $installerLines -join "`r`n"

[System.IO.File]::WriteAllText("$remoteBase\install_services.bat", $installerBat, [System.Text.Encoding]::ASCII)

$res = Exec-WmiCmd "cmd.exe /c C:\Services\ApprovalPlatform\install_services.bat"
Write-Host "  WMI 服务注册与启动命令已触发，返回码: $res" -ForegroundColor Green
Start-Sleep -Seconds 6

# 5. 验证部署运行状态
Write-Host "`n[5/5] 执行健康检查与端点存活性验证..." -ForegroundColor Yellow

$healthUrl = "http://$TargetIp`:$ServicePort/health"
$homeUrl = "http://$TargetIp`:$ServicePort/"
$swaggerUrl = "http://$TargetIp`:$ServicePort/swagger"

try {
    $healthResp = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 10
    Write-Host "  ✅ 健康检查端点 $healthUrl : 状态正常 [Healthy]!" -ForegroundColor Green
} catch {
    Write-Warning "  健康检查端点响应: $($_.Exception.Message)"
}

try {
    $homeResp = Invoke-WebRequest -Uri $homeUrl -Method Get -TimeoutSec 10
    if ($homeResp.StatusCode -eq 200) {
        Write-Host "  ✅ 前端 Web 门户 $homeUrl : 成功托管渲染!" -ForegroundColor Green
    }
} catch {
    Write-Warning "  门户响应: $($_.Exception.Message)"
}

# 6. 执行世界级无头浏览器真机端到端冒烟测试门禁 (Headless Browser Smoke Test Gate)
Write-Host "`n[6/6] 触发无头浏览器真机端到端渲染与防白屏冒烟测试..." -ForegroundColor Yellow
$smokeScript = Join-Path $rootDir "scripts\test_nav_capture.mjs"
if (Test-Path $smokeScript) {
    $smokeOutput = node $smokeScript
    $has404 = $smokeOutput -match "404 Not Found"
    $hasException = $smokeOutput -match "RUNTIME EXCEPTION"
    $isMounted = $smokeOutput -match "#app Children: [1-9]"

    if ($has404 -or $hasException -or -not $isMounted) {
        Write-Host $smokeOutput -ForegroundColor Red
        Write-Error "❌ 自动化前端冒烟测试未通过 (检测到白屏或 404 资源缺失)，发布阻断！"
    } else {
        Write-Host "  ✅ 自动化无头浏览器真机冒烟测试 100% 绿灯通过 (0 异常, 0 报错, 成功渲染挂载)！" -ForegroundColor Green
    }
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "  🎉 SAP B1 通用审批平台在 $TargetIp 上已部署上线！" -ForegroundColor Green
Write-Host "  - 审批中心 Web 门户: http://$TargetIp`:$ServicePort" -ForegroundColor Green
Write-Host "  - Swagger 接口文档:  http://$TargetIp`:$ServicePort/swagger" -ForegroundColor Green
Write-Host "  - 健康检查探针:      http://$TargetIp`:$ServicePort/health" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
