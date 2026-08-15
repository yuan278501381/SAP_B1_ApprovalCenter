/* 已执行过旧版 01 脚本时使用；全新数据库只需执行新版 01。文件编码：GBK 编码。 */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.wf_outbox', 'processing_at') IS NULL
    ALTER TABLE dbo.wf_outbox ADD processing_at DATETIME2(3) NULL;
IF COL_LENGTH('dbo.wf_outbox', 'lock_id') IS NULL
    ALTER TABLE dbo.wf_outbox ADD lock_id VARCHAR(64) NULL;
IF COL_LENGTH('dbo.wf_outbox', 'row_version') IS NULL
    ALTER TABLE dbo.wf_outbox ADD row_version ROWVERSION;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.wf_instance') AND name = 'UX_wf_instance_running_object')
    CREATE UNIQUE INDEX UX_wf_instance_running_object
        ON dbo.wf_instance(company_id, object_code, object_key) WHERE status = 'Running';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.wf_task_candidate') AND name = 'UQ_wf_candidate_task_user')
    CREATE UNIQUE INDEX UQ_wf_candidate_task_user ON dbo.wf_task_candidate(task_id, user_code);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.wf_inbox') AND name = 'UQ_wf_inbox_handler_key')
    CREATE UNIQUE INDEX UQ_wf_inbox_handler_key ON dbo.wf_inbox(handler_name, idempotency_key);

COMMIT TRANSACTION;
