<#
.SYNOPSIS
    SAP B1 审批中心数据库 ApprovalDB 热备份、快照与灾难恢复脚本
.DESCRIPTION
    支持：全量备份 (Full Backup)、差异备份 (Diff Backup)、事务日志备份 (Log Backup) 与一键恢复验证
.PARAMETER Mode
    操作模式: Backup 或 Restore (默认 Backup)
.PARAMETER BackupType
    备份类型: Full, Diff, Log (默认 Full)
.PARAMETER SqlServerInstance
    SQL Server 实例地址 (默认 192.168.134.9)
.PARAMETER BackupDir
    备份文件存放目录 (默认 C:\SQL_Backups\ApprovalDB)
#>
param(
    [ValidateSet("Backup", "Restore")]
    [string]$Mode = "Backup",

    [ValidateSet("Full", "Diff", "Log")]
    [string]$BackupType = "Full",

    [string]$SqlServerInstance = "192.168.134.9",
    [string]$SqlSaPassword = "123456@a",
    [string]$BackupDir = "C:\SQL_Backups\ApprovalDB"
)

$ErrorActionPreference = "Stop"
$timestamp = (Get-Date).ToString("yyyyMMdd_HHmmss")

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  SAP B1 ApprovalDB 数据库运维与灾难恢复管理工具" -ForegroundColor Cyan
Write-Host "  模式: $Mode | 类型: $BackupType | 实例: $SqlServerInstance" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

$connStr = "Server=$SqlServerInstance;Database=master;User Id=sa;Password=$SqlSaPassword;TrustServerCertificate=True;Timeout=300;"
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)

try {
    $conn.Open()
    $cmd = $conn.CreateCommand()

    if ($Mode -eq "Backup") {
        $backupFileName = "$BackupDir\ApprovalDB_${BackupType}_$timestamp.bak"
        Write-Host "`n正在执行 ApprovalDB $BackupType 备份至 $backupFileName..." -ForegroundColor Yellow

        $sql = switch ($BackupType) {
            "Full" { "BACKUP DATABASE [ApprovalDB] TO DISK = N'$backupFileName' WITH FORMAT, INIT, COMPRESSION, CHECKSUM, STATS = 10;" }
            "Diff" { "BACKUP DATABASE [ApprovalDB] TO DISK = N'$backupFileName' WITH DIFFERENTIAL, COMPRESSION, CHECKSUM, STATS = 10;" }
            "Log"  { "BACKUP LOG [ApprovalDB] TO DISK = N'$backupFileName' WITH COMPRESSION, CHECKSUM, STATS = 10;" }
        }

        $cmd.CommandText = $sql
        $cmd.ExecuteNonQuery() | Out-Null
        Write-Host "✅ 备份成功！备份文件: $backupFileName" -ForegroundColor Green
    }
    elseif ($Mode -eq "Restore") {
        Write-Warning "⚠️ 正在准备恢复 ApprovalDB 数据库，这将会断开现有连接并重置数据！"
        $latestBak = Get-ChildItem -Path $BackupDir -Filter "ApprovalDB_Full_*.bak" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        
        if (!$latestBak) {
            Write-Error "在 $BackupDir 中未找到任何可用的全量备份文件！"
        }

        $restoreFile = $latestBak.FullName
        Write-Host "`n正在从最新快照 $restoreFile 执行完整恢复..." -ForegroundColor Yellow

        $cmd.CommandText = @"
ALTER DATABASE [ApprovalDB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [ApprovalDB] FROM DISK = N'$restoreFile' WITH REPLACE, RECOVERY, STATS = 10;
ALTER DATABASE [ApprovalDB] SET MULTI_USER;
"@
        $cmd.ExecuteNonQuery() | Out-Null
        Write-Host "✅ 数据库已成功从备份快照恢复！" -ForegroundColor Green
    }

} catch {
    Write-Error "数据库操作异常: $($_.Exception.Message)"
} finally {
    $conn.Close()
}
