USE [ApprovalDB];
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'sys_ui_layout')
BEGIN
    CREATE TABLE [dbo].[sys_ui_layout] (
        [id] VARCHAR(128) NOT NULL PRIMARY KEY,
        [company_id] VARCHAR(64) NOT NULL,
        [object_code] VARCHAR(64) NOT NULL,
        [user_code] VARCHAR(64) NULL,
        [layout_json] NVARCHAR(MAX) NOT NULL,
        [updated_at] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [updated_by] VARCHAR(64) NOT NULL
    );
    CREATE INDEX [IX_sys_ui_layout_lookup] ON [dbo].[sys_ui_layout]([company_id], [object_code], [user_code]);
    PRINT 'TABLE sys_ui_layout CREATED SUCCESSFULLY';
END
ELSE
BEGIN
    PRINT 'TABLE sys_ui_layout ALREADY EXISTS';
END
GO
