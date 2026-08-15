/*
  SAP B1 ????????? ApprovalDB ????????
  ???SQL Server 2019+?????????GBK ????
  ?????????????§Ý??????? ApprovalDB??????? SAP ?????????§³?
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.wf_definition', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_definition (
        id VARCHAR(64) NOT NULL CONSTRAINT PK_wf_definition PRIMARY KEY,
        name NVARCHAR(128) NOT NULL,
        category NVARCHAR(64) NOT NULL CONSTRAINT DF_wf_definition_category DEFAULT N'General',
        description NVARCHAR(500) NULL,
        is_active BIT NOT NULL CONSTRAINT DF_wf_definition_active DEFAULT 1,
        created_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_definition_created DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_definition_updated DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.wf_definition_version', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_definition_version (
        id VARCHAR(64) NOT NULL CONSTRAINT PK_wf_definition_version PRIMARY KEY,
        definition_id VARCHAR(64) NOT NULL,
        version_num INT NOT NULL,
        graph_json NVARCHAR(MAX) NOT NULL,
        status VARCHAR(32) NOT NULL CONSTRAINT DF_wf_version_status DEFAULT 'Draft',
        published_at DATETIME2(3) NULL,
        created_by NVARCHAR(64) NOT NULL,
        created_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_version_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_wf_version_definition FOREIGN KEY (definition_id) REFERENCES dbo.wf_definition(id),
        CONSTRAINT UQ_wf_version_number UNIQUE (definition_id, version_num)
    );
END;

IF OBJECT_ID(N'dbo.wf_binding', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_binding (
        id VARCHAR(64) NOT NULL CONSTRAINT PK_wf_binding PRIMARY KEY,
        company_id NVARCHAR(64) NOT NULL,
        object_code NVARCHAR(64) NOT NULL,
        version_id VARCHAR(64) NOT NULL,
        priority INT NOT NULL CONSTRAINT DF_wf_binding_priority DEFAULT 0,
        condition_expr NVARCHAR(500) NULL,
        is_active BIT NOT NULL CONSTRAINT DF_wf_binding_active DEFAULT 1,
        created_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_binding_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_wf_binding_version FOREIGN KEY (version_id) REFERENCES dbo.wf_definition_version(id)
    );
    CREATE INDEX IX_wf_binding_lookup ON dbo.wf_binding(company_id, object_code, is_active, priority DESC);
END;

IF OBJECT_ID(N'dbo.wf_instance', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_instance (
        id VARCHAR(64) NOT NULL CONSTRAINT PK_wf_instance PRIMARY KEY,
        company_id NVARCHAR(64) NOT NULL,
        object_code NVARCHAR(64) NOT NULL,
        object_key NVARCHAR(128) NOT NULL,
        title NVARCHAR(256) NOT NULL,
        submitter_code NVARCHAR(64) NOT NULL,
        submitter_name NVARCHAR(128) NULL,
        status VARCHAR(32) NOT NULL CONSTRAINT DF_wf_instance_status DEFAULT 'Running',
        current_version_id VARCHAR(64) NOT NULL,
        created_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_instance_created DEFAULT SYSUTCDATETIME(),
        finished_at DATETIME2(3) NULL,
        row_version ROWVERSION NOT NULL,
        CONSTRAINT FK_wf_instance_version FOREIGN KEY (current_version_id) REFERENCES dbo.wf_definition_version(id)
    );
    CREATE INDEX IX_wf_instance_object ON dbo.wf_instance(company_id, object_code, object_key, status);
    CREATE UNIQUE INDEX UX_wf_instance_running_object
        ON dbo.wf_instance(company_id, object_code, object_key) WHERE status = 'Running';
END;

IF OBJECT_ID(N'dbo.wf_snapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_snapshot (
        instance_id VARCHAR(64) NOT NULL CONSTRAINT PK_wf_snapshot PRIMARY KEY,
        raw_json NVARCHAR(MAX) NOT NULL,
        canonical_json NVARCHAR(MAX) NOT NULL,
        data_sha256 VARCHAR(64) NOT NULL,
        snapshotted_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_snapshot_time DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_wf_snapshot_instance FOREIGN KEY (instance_id) REFERENCES dbo.wf_instance(id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.wf_node_instance', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_node_instance (
        id VARCHAR(64) NOT NULL CONSTRAINT PK_wf_node_instance PRIMARY KEY,
        instance_id VARCHAR(64) NOT NULL,
        node_key VARCHAR(64) NOT NULL,
        node_name NVARCHAR(128) NOT NULL,
        node_type VARCHAR(32) NOT NULL,
        status VARCHAR(32) NOT NULL CONSTRAINT DF_wf_node_status DEFAULT 'Pending',
        started_at DATETIME2(3) NULL,
        completed_at DATETIME2(3) NULL,
        CONSTRAINT FK_wf_node_instance FOREIGN KEY (instance_id) REFERENCES dbo.wf_instance(id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.wf_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_task (
        id VARCHAR(64) NOT NULL CONSTRAINT PK_wf_task PRIMARY KEY,
        instance_id VARCHAR(64) NOT NULL,
        node_instance_id VARCHAR(64) NOT NULL,
        task_type VARCHAR(32) NOT NULL CONSTRAINT DF_wf_task_type DEFAULT 'Approve',
        status VARCHAR(32) NOT NULL CONSTRAINT DF_wf_task_status DEFAULT 'Pending',
        created_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_task_created DEFAULT SYSUTCDATETIME(),
        due_at DATETIME2(3) NULL,
        completed_at DATETIME2(3) NULL,
        completed_by NVARCHAR(64) NULL,
        decision VARCHAR(32) NULL,
        comments NVARCHAR(1000) NULL,
        row_version ROWVERSION NOT NULL,
        CONSTRAINT FK_wf_task_instance FOREIGN KEY (instance_id) REFERENCES dbo.wf_instance(id),
        CONSTRAINT FK_wf_task_node FOREIGN KEY (node_instance_id) REFERENCES dbo.wf_node_instance(id)
    );
    CREATE INDEX IX_wf_task_status ON dbo.wf_task(status, due_at);
END;

IF OBJECT_ID(N'dbo.wf_task_candidate', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_task_candidate (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_wf_task_candidate PRIMARY KEY,
        task_id VARCHAR(64) NOT NULL,
        user_code NVARCHAR(64) NOT NULL,
        user_name NVARCHAR(128) NULL,
        candidate_type VARCHAR(32) NOT NULL CONSTRAINT DF_wf_candidate_type DEFAULT 'Direct',
        CONSTRAINT FK_wf_candidate_task FOREIGN KEY (task_id) REFERENCES dbo.wf_task(id) ON DELETE CASCADE,
        CONSTRAINT UQ_wf_candidate_task_user UNIQUE (task_id, user_code)
    );
    CREATE INDEX IX_wf_candidate_user ON dbo.wf_task_candidate(user_code, task_id);
END;

IF OBJECT_ID(N'dbo.wf_action_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_action_log (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_wf_action_log PRIMARY KEY,
        trace_id VARCHAR(64) NOT NULL,
        instance_id VARCHAR(64) NOT NULL,
        task_id VARCHAR(64) NULL,
        operator_code NVARCHAR(64) NOT NULL,
        operator_name NVARCHAR(128) NULL,
        action VARCHAR(64) NOT NULL,
        from_status VARCHAR(32) NOT NULL,
        to_status VARCHAR(32) NOT NULL,
        comment NVARCHAR(1000) NULL,
        client_ip VARCHAR(64) NULL,
        action_time DATETIME2(3) NOT NULL CONSTRAINT DF_wf_action_log_time DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_wf_log_instance FOREIGN KEY (instance_id) REFERENCES dbo.wf_instance(id)
    );
    CREATE INDEX IX_wf_action_log_instance ON dbo.wf_action_log(instance_id, action_time);
END;

IF OBJECT_ID(N'dbo.wf_outbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_outbox (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_wf_outbox PRIMARY KEY,
        trace_id VARCHAR(64) NOT NULL,
        event_type VARCHAR(64) NOT NULL,
        aggregate_id VARCHAR(64) NOT NULL,
        payload_json NVARCHAR(MAX) NOT NULL,
        status VARCHAR(32) NOT NULL CONSTRAINT DF_wf_outbox_status DEFAULT 'Pending',
        retry_count INT NOT NULL CONSTRAINT DF_wf_outbox_retry DEFAULT 0,
        max_retries INT NOT NULL CONSTRAINT DF_wf_outbox_max_retry DEFAULT 10,
        next_retry_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_outbox_next_retry DEFAULT SYSUTCDATETIME(),
        error_msg NVARCHAR(MAX) NULL,
        created_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_outbox_created DEFAULT SYSUTCDATETIME(),
        sent_at DATETIME2(3) NULL,
        processing_at DATETIME2(3) NULL,
        lock_id VARCHAR(64) NULL,
        row_version ROWVERSION NOT NULL
    );
    CREATE INDEX IX_wf_outbox_poll ON dbo.wf_outbox(status, next_retry_at);
END;

IF OBJECT_ID(N'dbo.wf_inbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.wf_inbox (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_wf_inbox PRIMARY KEY,
        idempotency_key VARCHAR(128) NOT NULL,
        handler_name VARCHAR(128) NOT NULL,
        response_json NVARCHAR(MAX) NULL,
        processed_at DATETIME2(3) NOT NULL CONSTRAINT DF_wf_inbox_time DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_wf_inbox_handler_key UNIQUE (handler_name, idempotency_key)
    );
END;

IF OBJECT_ID(N'dbo.sap_sync_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sap_sync_state (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sap_sync_state PRIMARY KEY,
        company_id NVARCHAR(64) NOT NULL,
        object_code NVARCHAR(64) NOT NULL,
        object_key NVARCHAR(128) NOT NULL,
        instance_id VARCHAR(64) NOT NULL,
        expected_status VARCHAR(32) NOT NULL,
        last_synced_status VARCHAR(32) NULL,
        sync_status VARCHAR(32) NOT NULL CONSTRAINT DF_sap_sync_status DEFAULT 'Pending',
        last_sync_attempt DATETIME2(3) NULL,
        error_message NVARCHAR(MAX) NULL,
        CONSTRAINT UQ_sap_sync_object UNIQUE (company_id, object_code, object_key)
    );
END;

COMMIT TRANSACTION;
