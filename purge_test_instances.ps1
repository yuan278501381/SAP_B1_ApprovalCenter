$connStr = 'Server=192.168.134.9;Database=ApprovalDB;User Id=sa;Password=123456@a;TrustServerCertificate=True;'
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

$sql = @"
DECLARE @TestInstances TABLE (Id NVARCHAR(64));
INSERT INTO @TestInstances
SELECT id FROM wf_instance WHERE object_key LIKE 'TEST_%' OR object_key LIKE 'CONCUR_%' OR object_key LIKE '%_%' OR title LIKE '%北京中源嘉斯%';

DELETE FROM wf_task_candidate WHERE task_id IN (SELECT id FROM wf_task WHERE instance_id IN (SELECT Id FROM @TestInstances));
DELETE FROM wf_task WHERE instance_id IN (SELECT Id FROM @TestInstances);
DELETE FROM wf_action_log WHERE instance_id IN (SELECT Id FROM @TestInstances);
DELETE FROM wf_node_instance WHERE instance_id IN (SELECT Id FROM @TestInstances);
DELETE FROM wf_snapshot WHERE instance_id IN (SELECT Id FROM @TestInstances);
DELETE FROM wf_outbox WHERE aggregate_id IN (SELECT Id FROM @TestInstances);
DELETE FROM wf_instance WHERE id IN (SELECT Id FROM @TestInstances);
"@

$cmd = $conn.CreateCommand()
$cmd.CommandText = $sql
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "已物理清理所有测试前缀单据 (CONCUR_ / TEST_ / 北京中源嘉斯)！"

$cmd.CommandText = "SELECT id, object_code, object_key, title, status FROM wf_instance;"
$r = $cmd.ExecuteReader()
Write-Host "`n当前数据库中保留的审批单据:"
while ($r.Read()) {
    Write-Host "单据: $($r['object_code']) #$($r['object_key']) | 客户/标题: $($r['title']) | 状态: $($r['status'])"
}
$r.Close()
$conn.Close()
