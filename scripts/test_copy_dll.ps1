$TargetIp = "192.168.134.9"
$AdminUser = "administrator"
$AdminPassword = "123456@aA"

$netUse = "net use \\$TargetIp\c$ `"$AdminPassword`" /user:`"$AdminUser`""
cmd /c $netUse | Out-Null

$src = "C:\repo\SAP_B1_ApprovalCenter\dist\win-x64\Approval.Api\Approval.Api.dll"
$dst = "\\$TargetIp\c$\Services\ApprovalPlatform\Approval.Api\Approval.Api.dll"

Write-Host "本地 DLL: $(Get-Item $src | Select-Object LastWriteTimeUtc, Length | Out-String)"
Write-Host "远程 DLL: $(Get-Item $dst | Select-Object LastWriteTimeUtc, Length | Out-String)"

try {
    Write-Host "尝试通过 Copy-Item 覆盖远程 DLL..."
    Copy-Item $src -Destination $dst -Force
    Write-Host "Copy-Item 成功！最新远程 DLL 时间: $((Get-Item $dst).LastWriteTimeUtc)"
} catch {
    Write-Host "Copy-Item 失败: $($_.Exception.Message)" -ForegroundColor Red
}
