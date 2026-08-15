-- database/04_create_sys_user_mapping.sql
USE ApprovalDB;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'sys_user_mapping')
BEGIN
    CREATE TABLE dbo.sys_user_mapping (
        id VARCHAR(64) NOT NULL CONSTRAINT PK_sys_user_mapping PRIMARY KEY,
        sap_user_id INT NULL,
        sap_user_code NVARCHAR(64) NOT NULL,
        ad_user_code NVARCHAR(64) NOT NULL,
        display_name NVARCHAR(128) NOT NULL,
        department NVARCHAR(128) NULL,
        manager_code NVARCHAR(64) NULL,
        roles NVARCHAR(500) NULL,
        delegate_user_code NVARCHAR(64) NULL,
        delegate_start_time DATETIME2 NULL,
        delegate_end_time DATETIME2 NULL,
        is_active BIT NOT NULL CONSTRAINT DF_sys_user_mapping_is_active DEFAULT (1),
        created_at DATETIME2 NOT NULL CONSTRAINT DF_sys_user_mapping_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2 NOT NULL CONSTRAINT DF_sys_user_mapping_updated_at DEFAULT (SYSUTCDATETIME())
    );

    CREATE UNIQUE INDEX UX_sys_user_mapping_sap_user_code ON dbo.sys_user_mapping(sap_user_code);
    CREATE UNIQUE INDEX UX_sys_user_mapping_ad_user_code ON dbo.sys_user_mapping(ad_user_code);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.sys_user_mapping WHERE sap_user_code = 'manager')
BEGIN
    INSERT INTO dbo.sys_user_mapping (id, sap_user_id, sap_user_code, ad_user_code, display_name, department, manager_code, roles, is_active)
    VALUES ('USER_MGR', 1, 'manager', 'E001', N'张经理', N'销售部', 'director', N'SalesManager,GeneralApprover', 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.sys_user_mapping WHERE sap_user_code = 'director')
BEGIN
    INSERT INTO dbo.sys_user_mapping (id, sap_user_id, sap_user_code, ad_user_code, display_name, department, manager_code, roles, is_active)
    VALUES ('USER_DIR', 2, 'director', 'E002', N'业务总监', N'管理层', NULL, N'SalesDirector,ExecutiveApprover', 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.sys_user_mapping WHERE sap_user_code = '16836')
BEGIN
    INSERT INTO dbo.sys_user_mapping (id, sap_user_id, sap_user_code, ad_user_code, display_name, department, manager_code, roles, is_active)
    VALUES ('USER_16836', 200, '16836', 'E16836', N'王桃园', N'销售一部', 'manager', N'SalesRepresentative', 1);
END
GO
