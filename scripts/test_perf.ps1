$headers = @{
    "X-Approval-User" = "manager"
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$tasks = Invoke-RestMethod -Uri "http://192.168.134.9:5000/api/v1/tasks?scope=pending&companyId=DB_KCC" -Headers $headers
$sw.Stop()
Write-Host "1. 获取待办任务列表耗时: $($sw.ElapsedMilliseconds) ms, 任务数: $($tasks.data.items.Count)"

if ($tasks.data.items.Count -gt 0) {
    $firstTaskId = $tasks.data.items[0].taskId
    $sw.Restart()
    $detail = Invoke-RestMethod -Uri "http://192.168.134.9:5000/api/v1/tasks/$firstTaskId" -Headers $headers
    $sw.Stop()
    Write-Host "2. 获取第一条单据完整快照详情耗时: $($sw.ElapsedMilliseconds) ms"
    Write-Host "   - 单据标题: $($detail.data.instance.title)"
    Write-Host "   - 快照字节大小: $($detail.data.snapshot.rawJson.Length) 字节"
    Write-Host "   - 审计日志条数: $($detail.data.auditLogs.Count)"
}
