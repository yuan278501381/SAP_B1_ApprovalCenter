$headers = @{
    "Content-Type" = "application/json"
    "X-Approval-User" = "manager"
}
$body = '{"companyId":"DB_KCC","objectCode":"CHORDR","layoutJson":"{}"}'

try {
    $resp = Invoke-RestMethod -Uri "http://192.168.134.9:5000/api/v1/ui-layouts" -Method POST -Headers $headers -Body $body -TimeoutSec 10
    $resp | ConvertTo-Json -Depth 5
} catch {
    Write-Host "Caught Error: $($_.Exception.Message)"
    if ($_.ErrorDetails) {
        Write-Host "ErrorDetails: $($_.ErrorDetails.Message)"
    }
}
