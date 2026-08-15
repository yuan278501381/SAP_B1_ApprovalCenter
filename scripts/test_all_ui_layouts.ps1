$headers = @{
    "Content-Type" = "application/json"
    "X-Approval-User" = "admin"
}
$body = '{"companyId":"DB_KCC","objectCode":"CHORDR","layoutJson":"{\"test\":true}"}'

Write-Host "1. 测试保存个人偏好:"
$resp1 = Invoke-RestMethod -Uri "http://192.168.134.9:5000/api/v1/ui-layouts" -Method POST -Headers $headers -Body $body
$resp1 | ConvertTo-Json

Write-Host "`n2. 测试保存全公司全局默认配置 (Admin):"
$resp2 = Invoke-RestMethod -Uri "http://192.168.134.9:5000/api/v1/ui-layouts/global" -Method POST -Headers $headers -Body $body
$resp2 | ConvertTo-Json

Write-Host "`n3. 测试获取生效配置:"
$resp3 = Invoke-RestMethod -Uri "http://192.168.134.9:5000/api/v1/ui-layouts?companyId=DB_KCC&objectCode=CHORDR" -Method GET -Headers $headers
$resp3 | ConvertTo-Json -Depth 5
