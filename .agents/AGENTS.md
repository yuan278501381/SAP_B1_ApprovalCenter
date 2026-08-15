<RULE[project_business_context]>
## 1. 项目核心背景与业务现状 (Business Context)
* **系统环境**: SAP Business One 10.0 (版本 10.00.321) SQL Server 版。
* **业务数据库**: SAP 业务公司库 `DB_KCC`；审批平台独立数据库 `ApprovalDB`（严禁在 SAP 公司库混建审批业务表，保持物理隔离）。
* **首期验证对象**:
  1. 型号订单 UDO: `CHORDR`（主表 `dbo.[@CH_ORDR]`，子表 `dbo.[@CH_ORDR_1]`、`dbo.[@CH_ORDR_3]`，Document 类型，`CanNewForm=Y`）。
  2. 型号报价单 UDO: `CHOQUT`（作为通用性扩展验证对象）。
* **技术栈**: .NET 8 (C# 12) DDD 整洁架构 + Vue 3 TypeScript 响应式前端 + SQL Server 2022 (EF Core 8 / Outbox)。
</RULE[project_business_context]>

<RULE[auxiliary_platform_constraints]>
## 2. 第三方辅助开发平台黑盒约束与交互准则 (Platform Constraints)
* **无源码黑盒约束**: 辅助平台没有源代码，严禁假设可以修改平台代码、注入 Add-on 命令或通过 JS Bridge/WebSocket 反向操控 SAP 客户端窗口。
* **已确认可用的平台核心能力**:
  1. 在 SAP 自定义单据窗口中放置 WEB 浏览器控件；
  2. 通过命令按钮执行“刷新表头数据”，可根据当前控件值动态为 WEB 控件赋值 URL；
  3. 无对象窗口加载时，查询表头可动态为 WEB 控件赋值 URL；
  4. 查询表格支持“文本框（带链接）/ 黄箭头”，可按当前行取值穿透打开 SAP 标准单据或平台自定义窗口。
* **交互闭环标准**:
  - **打开单据**: 不在网页内部放“打开 SAP 窗口”按钮，而是引导用户点击 SAP 审批工作台表格中的【黄色箭头】直接穿透打开原始单据。
  - **状态同步**: 网页审批完成后，由 Service Layer 异步回写 SAP 镜像字段，用户在 SAP 中点击现有“刷新表头”查看最新状态。
  - **显式提交**: 严禁“页面加载即自动发起审批”，所有审批提交必须由用户在界面显式点击触发。
</RULE[auxiliary_platform_constraints]>

<RULE[reliability_and_security_rules]>
## 3. 架构可靠性与安全规则 (Reliability & Security)
* **规范化快照与 SHA-256 防篡改 (Canonical Snapshot)**:
  - 提交审批时，由服务端通过 Service Layer 抓取完整单据（表头+所有子表），属性按字典序递归排序列化为 Canonical JSON 并计算 SHA-256 哈希固化在 `wf_snapshot`。
  - 审批通过回写前重新校验哈希，若单据在审批流转期间被私自篡改，立即熔断阻断放行。
* **Outbox 事务发件箱模式 (Outbox Pattern)**:
  - 严禁使用跨库分布式 2PC 事务。所有审批状态流转均由本地事务持久化到 `wf_outbox`，后台 Worker 异步回写 SAP 并保证 At-least-once 投递与业务幂等。
* **字段变更免审分级 (Field Sensitivity & Delta Whitelist)**:
  - **免审白名单字段**（如备注 `Comments`、物流单号、打印次数）：修改后直接放行，仅记录审计日志，不重新触发审批风暴。
  - **核心敏感字段**（如单价 `Price`、数量 `Quantity`、总金额 `DocTotal`、客户 `CardCode`）：修改后单据自动置为待重审 (`Pending/Re-Approving`) 并重新锁定下游动作。
* **数据库编码红线**: 项目中所有 `.sql` 文件历史基准均为 **GBK 编码**，严禁使用 UTF-8 导致中文乱码。
* **全链路可观测性 (Observability)**: 全链路注入并透传 `TraceID`，区分日志等级 (DEBUG, INFO, WARN, ERROR, FATAL)。
</RULE[reliability_and_security_rules]>

<RULE[local_vm_environment]>
## 4. 本地测试虚拟机连接信息 (Test Environment)
* **IP 地址**: 192.168.134.9
* **操作系统**: Windows Server 2025
* **系统管理员账号**: administrator
* **系统管理员密码**: 123456@aA
* **数据库版本**: SQL Server 2022
* **SA 账号**: sa
* **SA 密码**: 123456@a
* **SSH 免密直连**: `ssh -o StrictHostKeyChecking=no administrator@192.168.134.9 "<command>"`
</RULE[local_vm_environment]>
