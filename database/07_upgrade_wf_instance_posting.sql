IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.wf_instance') AND name = 'target_doc_type')
BEGIN
    ALTER TABLE dbo.wf_instance ADD target_doc_type NVARCHAR(64) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.wf_instance') AND name = 'posted_doc_entry')
BEGIN
    ALTER TABLE dbo.wf_instance ADD posted_doc_entry NVARCHAR(128) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.wf_instance') AND name = 'posted_doc_num')
BEGIN
    ALTER TABLE dbo.wf_instance ADD posted_doc_num NVARCHAR(128) NULL;
END
GO
