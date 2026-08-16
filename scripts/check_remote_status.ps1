$sec = ConvertTo-SecureString "123456@aA" -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential("administrator", $sec)
$opt = New-CimSessionOption -Protocol DCOM
$sess = New-CimSession -ComputerName "192.168.134.9" -Credential $cred -SessionOption $opt

Write-Host "=== 远程服务状态 ==="
Get-CimInstance -CimSession $sess -ClassName Win32_Service -Filter "Name LIKE '%Approval%'" | Select-Object Name, State, ProcessId, PathName | Format-Table

Write-Host "=== 远程进程状态 ==="
Get-CimInstance -CimSession $sess -ClassName Win32_Process -Filter "Name LIKE '%Approval%' OR Name LIKE '%dotnet%'" | Select-Object ProcessId, Name, CommandLine | Format-Table
