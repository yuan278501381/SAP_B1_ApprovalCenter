$connStr = "Server=192.168.134.9;Database=ApprovalDB;User Id=sa;Password=123456@a;TrustServerCertificate=True;"
$sql = @"
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'sys_ui_layout')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('sys_ui_layout') AND name = 'config_type')
    BEGIN
        ALTER TABLE [dbo].[sys_ui_layout] ADD [config_type] VARCHAR(64) NOT NULL DEFAULT 'HeaderAndTableLayout';
        PRINT 'COLUMN config_type ADDED';
    END
    ELSE
    BEGIN
        PRINT 'COLUMN config_type ALREADY EXISTS';
    END
END
ELSE
BEGIN
    CREATE TABLE [dbo].[sys_ui_layout] (
        [id] VARCHAR(128) NOT NULL PRIMARY KEY,
        [company_id] VARCHAR(64) NOT NULL,
        [object_code] VARCHAR(64) NOT NULL,
        [user_code] VARCHAR(64) NULL,
        [config_type] VARCHAR(64) NOT NULL DEFAULT 'HeaderAndTableLayout',
        [layout_json] NVARCHAR(MAX) NOT NULL,
        [updated_at] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [updated_by] VARCHAR(64) NOT NULL
    );
    CREATE INDEX [IX_sys_ui_layout_lookup] ON [dbo].[sys_ui_layout]([company_id], [object_code], [user_code]);
    PRINT 'TABLE sys_ui_layout CREATED';
END
"@

$conn = New-Object Microsoft.Data.SqlClient.SqlConnection($connStr)
if ($conn.State -ne 'Open') {
    # 兜底使用 System.Data.SqlClient
    try {
        $conn.Open()
    } catch {
        $conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
        $conn.Open()
    }
}

$cmd = $conn.CreateCommand()
$cmd.CommandText = $sql
$res = $cmd.ExecuteNonQuery()
Write-Host "SQL Executed successfully! (Result: $res)"
$conn.Close()
