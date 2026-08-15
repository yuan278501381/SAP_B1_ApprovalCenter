-- ==============================================================================
-- 审批中心生产业务门禁：SBO_SP_TransactionNotification
-- 数据库适用：DB_KCC (SAP Business One 10.0 公司业务库)
-- 编码要求：GBK 编码（严禁使用 UTF-8，防止在 SAP 客户端报错弹窗时出现中文乱码）
-- ==============================================================================

USE DB_KCC;
GO

-- 门禁实现逻辑片段（可直接合并或嵌入到现有 SBO_SP_TransactionNotification 中）
/*
--------------------------------------------------------------------------------------------------------------------------------
-- 门禁规则 1: 型号订单 (CHORDR) 状态与下游生成拦截
-- 当用户尝试基于型号订单生成标准销售订单或直接修改关键状态时，校验审批状态必须为 Approved
--------------------------------------------------------------------------------------------------------------------------------
IF @transaction_type IN ('A', 'U') AND @object_type = 'CHORDR'
BEGIN
    -- 若单据已有审批流转记录但非 Approved 状态，禁止锁定或生成后续单据
    DECLARE @chordr_status NVARCHAR(32), @chordr_hash NVARCHAR(128);
    SELECT @chordr_status = U_APStatus, @chordr_hash = U_APHash
    FROM dbo.[@CH_ORDR]
    WHERE DocEntry = CAST(@list_of_cols_val_tab_del AS INT);

    -- 规则说明：若配置了强门禁策略且状态不为 Approved，则阻断放行
    -- 现场可根据业务需要放开草稿录入，但在生效/放行环节严格执行如下校验：
    /*
    IF ISNULL(@chordr_status, '') <> 'Approved'
    BEGIN
        SET @error = 10001;
        SET @error_message = N'【审批中心阻断】型号订单 [' + CAST(@list_of_cols_val_tab_del AS NVARCHAR(30)) + N'] 尚未审批通过（当前状态：' + ISNULL(@chordr_status, N'待审批') + N'），禁止保存或放行！';
        RETURN;
    END
    */
END;

--------------------------------------------------------------------------------------------------------------------------------
-- 门禁规则 2: 标准销售订单 (ORDR, ObjectType=17) 来源引用型号订单校验
-- 当生成标准销售订单 (ORDR) 且其来源字段 U_CHDocEntry / U_SoEntry 引用了型号订单时，强制校验源订单必须已 Approved
--------------------------------------------------------------------------------------------------------------------------------
IF @transaction_type = 'A' AND @object_type = '17'
BEGIN
    IF EXISTS (
        SELECT 1 
        FROM ORDR o
        JOIN dbo.[@CH_ORDR] c ON o.U_SoEntry = c.DocEntry
        WHERE o.DocEntry = CAST(@list_of_cols_val_tab_del AS INT)
          AND ISNULL(c.U_APStatus, '') <> 'Approved'
    )
    BEGIN
        SET @error = 10002;
        SET @error_message = N'【审批中心阻断】引用的源型号订单尚未通过最终审批，禁止生成标准销售订单！';
        RETURN;
    END;
END;
*/
GO
