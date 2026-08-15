-- ============================================================================
-- 05_create_wf_rule.sql (GBK ????)
-- ????????????¡¤????????? DDL
-- ============================================================================
USE [ApprovalDB];
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.wf_rule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_rule (
        id VARCHAR(64) NOT NULL CONSTRAINT PK_wf_rule PRIMARY KEY,
        company_id NVARCHAR(64) NOT NULL,
        object_code NVARCHAR(64) NOT NULL,
        object_type NVARCHAR(32) NOT NULL CONSTRAINT DF_wf_rule_objtype DEFAULT 'Document',
        rule_name NVARCHAR(128) NOT NULL,
        description NVARCHAR(500) NULL,
        trigger_mode NVARCHAR(32) NOT NULL CONSTRAINT DF_wf_rule_triggermode DEFAULT 'AutoAlways',
        trigger_field_name NVARCHAR(64) NULL,
        user_scope_mode VARCHAR(32) NOT NULL CONSTRAINT DF_wf_rule_userscopemode DEFAULT 'All',
        user_scope_list_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_wf_rule_userlist DEFAULT '[]',
        dept_scope_list_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_wf_rule_deptlist DEFAULT '[]',
        condition_expr NVARCHAR(500) NULL,
        target_definition_id VARCHAR(64) NOT NULL,
        target_version_id VARCHAR(64) NULL,
        priority INT NOT NULL CONSTRAINT DF_wf_rule_priority DEFAULT 10,
        is_active BIT NOT NULL CONSTRAINT DF_wf_rule_active DEFAULT 1,
        created_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_rule_created DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_rule_updated DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_wf_rule_match ON dbo.wf_rule(company_id, object_code, is_active, priority);
    PRINT 'Table dbo.wf_rule created successfully.';
END
ELSE
BEGIN
    PRINT 'Table dbo.wf_rule already exists.';
END