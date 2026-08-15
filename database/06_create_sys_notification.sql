-- ============================================================================
-- 06_create_sys_notification.sql (GBK ????)
-- ????????????????????? DDL
-- ============================================================================
USE [ApprovalDB];
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.sys_notification', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sys_notification (
        id VARCHAR(64) NOT NULL CONSTRAINT PK_sys_notification PRIMARY KEY,
        recipient_user_code VARCHAR(64) NOT NULL,
        sender_user_code VARCHAR(64) NOT NULL CONSTRAINT DF_sys_notif_sender DEFAULT 'system',
        instance_id VARCHAR(64) NULL,
        object_code VARCHAR(64) NULL,
        object_key VARCHAR(128) NULL,
        title NVARCHAR(256) NOT NULL,
        content NVARCHAR(MAX) NOT NULL,
        type VARCHAR(32) NOT NULL CONSTRAINT DF_sys_notif_type DEFAULT 'Revocation',
        is_read BIT NOT NULL CONSTRAINT DF_sys_notif_read DEFAULT 0,
        created_at DATETIME2(3) NOT NULL CONSTRAINT DF_sys_notif_created DEFAULT SYSUTCDATETIME(),
        read_at DATETIME2(3) NULL
    );
    CREATE INDEX IX_sys_notification_inbox ON dbo.sys_notification(recipient_user_code, is_read, created_at DESC);
    PRINT 'Table dbo.sys_notification created successfully.';
END
ELSE
BEGIN
    PRINT 'Table dbo.sys_notification already exists.';
END