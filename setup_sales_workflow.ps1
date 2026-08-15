$connStr = 'Server=192.168.134.9;Database=ApprovalDB;User Id=sa;Password=123456@a;TrustServerCertificate=True;'
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

# 1. 插入用户映射 (sys_user_mapping)
$userSql = @"
IF NOT EXISTS (SELECT 1 FROM sys_user_mapping WHERE sap_user_code = 'SALE01')
    INSERT INTO sys_user_mapping (id, sap_user_id, sap_user_code, ad_user_code, display_name, department, roles, is_active, created_at)
    VALUES ('USR_SALE01', '24', 'SALE01', 'SALE01', N'業助主管-朱躍南', N'销售业助部', '["sales_mgr","approver"]', 1, GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM sys_user_mapping WHERE sap_user_code = 'SALE02')
    INSERT INTO sys_user_mapping (id, sap_user_id, sap_user_code, ad_user_code, display_name, department, roles, is_active, created_at)
    VALUES ('USR_SALE02', '25', 'SALE02', 'SALE02', N'范冬梅', N'销售业助部', '["sales_ass","approver"]', 1, GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM sys_user_mapping WHERE sap_user_code = 'SALE03')
    INSERT INTO sys_user_mapping (id, sap_user_id, sap_user_code, ad_user_code, display_name, department, roles, is_active, created_at)
    VALUES ('USR_SALE03', '26', 'SALE03', 'SALE03', N'吴鑫梅', N'销售部', '["sales_rep"]', 1, GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM sys_user_mapping WHERE sap_user_code = 'SALE04')
    INSERT INTO sys_user_mapping (id, sap_user_id, sap_user_code, ad_user_code, display_name, department, roles, is_active, created_at)
    VALUES ('USR_SALE04', '27', 'SALE04', 'SALE04', N'吴小平', N'销售部', '["sales_rep"]', 1, GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM sys_user_mapping WHERE sap_user_code = 'manager')
    INSERT INTO sys_user_mapping (id, sap_user_id, sap_user_code, ad_user_code, display_name, department, roles, is_active, created_at)
    VALUES ('USR_MANAGER', '1', 'manager', 'manager', N'系统管理员-manager', N'管理部', '["admin","approver"]', 1, GETUTCDATE());
"@
$cmd = $conn.CreateCommand()
$cmd.CommandText = $userSql
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "1. 用户映射写入完成！"

# 2. 写入流程定义与数值枚举格式图 JSON
$graphObj = @{
    Nodes = @(
        @{ NodeKey = "start"; Name = "销售单据提交"; NodeType = 1 },
        @{ 
            NodeKey = "appr_sales_assist"; 
            Name = "业助审批 (朱躍南 / 范冬梅)"; 
            NodeType = 2; 
            TaskType = 1;
            CandidateValues = @("SALE01", "SALE02", "manager") 
        },
        @{ NodeKey = "end"; Name = "审批通过放行"; NodeType = 7 }
    );
    Edges = @(
        @{ FromNodeKey = "start"; ToNodeKey = "appr_sales_assist"; Label = "提交待审" },
        @{ FromNodeKey = "appr_sales_assist"; ToNodeKey = "end"; Label = "同意" }
    )
}
$graphJson = ($graphObj | ConvertTo-Json -Depth 5)

$defSql = @"
DELETE FROM wf_binding WHERE object_code IN ('CHORDR', 'CHOQUT', 'ORDR');
DELETE FROM wf_rule WHERE object_code IN ('CHORDR', 'CHOQUT', 'ORDR');
DELETE FROM wf_definition_version WHERE definition_id = 'DEF_SALES_ORDER';
DELETE FROM wf_definition WHERE id = 'DEF_SALES_ORDER';

INSERT INTO wf_definition (id, name, category, description, is_active, created_at)
VALUES ('DEF_SALES_ORDER', N'销售订单及报价单 (业助审批流)', 'Sales', N'完全参考 SAP B1 DB_KCC 标准审批模板 (WtmCode 9) 配置，由业助主管朱躍南 / 范冬梅 / manager 审核放行', 1, GETUTCDATE());

INSERT INTO wf_definition_version (id, definition_id, version_num, graph_json, status, published_at, created_by, created_at)
VALUES ('VER_SALES_ORDER_V1', 'DEF_SALES_ORDER', 1, N'$($graphJson.Replace("'", "''"))', 'Published', GETUTCDATE(), 'system', GETUTCDATE());

-- 绑定 CHORDR (型号订单)
INSERT INTO wf_binding (id, company_id, object_code, version_id, priority, is_active, created_at)
VALUES ('BIND_CHORDR_PROD', 'DB_KCC', 'CHORDR', 'VER_SALES_ORDER_V1', 10, 1, GETUTCDATE());

-- 绑定 CHOQUT (型号报价单)
INSERT INTO wf_binding (id, company_id, object_code, version_id, priority, is_active, created_at)
VALUES ('BIND_CHOQUT_PROD', 'DB_KCC', 'CHOQUT', 'VER_SALES_ORDER_V1', 10, 1, GETUTCDATE());

-- 规则矩阵：CHORDR 全员无条件触发
INSERT INTO wf_rule (id, company_id, object_code, object_type, rule_name, description, trigger_mode, trigger_field_name, user_scope_mode, user_scope_list_json, dept_scope_list_json, target_definition_id, target_version_id, priority, is_active, created_at)
VALUES ('RULE_CHORDR_SALES', 'DB_KCC', 'CHORDR', 'Document', N'型号订单业助审批规则', N'对应 SAP B1 销售订单及报价单审批模板，全员自动发起', 'AutoAlways', 'U_APSubmit', 'All', '[]', '[]', 'DEF_SALES_ORDER', 'VER_SALES_ORDER_V1', 10, 1, GETUTCDATE());

-- 规则矩阵：CHOQUT 全员无条件触发
INSERT INTO wf_rule (id, company_id, object_code, object_type, rule_name, description, trigger_mode, trigger_field_name, user_scope_mode, user_scope_list_json, dept_scope_list_json, target_definition_id, target_version_id, priority, is_active, created_at)
VALUES ('RULE_CHOQUT_SALES', 'DB_KCC', 'CHOQUT', 'Document', N'型号报价单业助审批规则', N'对应 SAP B1 销售订单及报价单审批模板，全员自动发起', 'AutoAlways', 'U_APSubmit', 'All', '[]', '[]', 'DEF_SALES_ORDER', 'VER_SALES_ORDER_V1', 10, 1, GETUTCDATE());
"@

$cmd.CommandText = $defSql
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "2. 流程定义、BPMN 版本、单据绑定与全员规则矩阵写入完成！"

$conn.Close()
