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

<RULE[code_coverage_and_quality_gates]>
## 4. 世界级代码覆盖率标准与质量门禁 (Code Coverage & Quality Gates)
* **高价值业务驱动原则**:
  - 绝不为了刷行数去测试无逻辑的样板代码；
  - 统一通过 `[ExcludeFromCodeCoverage]` 明确排除：EF Core `Migrations`、数据传输对象（DTOs）、配置 Options 绑定类及 `Program.cs` 样板代码。
* **分层质量门禁基准 (Quality Gate Baselines: 95%+)**:
  1. **👑 核心领域层 (`Approval.Domain`)**: 综合覆盖率目标 $\ge 95\% \sim 98\%$（必须 100% 覆盖 SHA-256 规范化快照、防篡改熔断、审批全状态机流转与 Outbox 幂等状态）；
  2. **🎯 规则矩阵与应用服务层 (`Approval.Application`)**: 覆盖率目标 $\ge 95\%$（覆盖单值比较操作符、子表高级聚合 `Sum`/`Avg`/`Min`/`Max`/`Count`、`Any`/`All` 模式、条件网关与主管追溯）；
  3. **🛡️ 适配器与基础设施层 (`Approval.Infrastructure` / `SapAdapter`)**: 覆盖率目标 $\ge 95\%$（覆盖多级缓存命中、磁盘持久化原子落盘容灾与 Service Layer 状态同步）。
* **DevOps CI/CD 自动化门禁**:
  - 打包流水线必须自动执行 `dotnet test --collect:"XPlat Code Coverage"` 并解析 Cobertura XML 报告；
  - 若核心业务覆盖率低于 95%，严禁放行构建与分发。
</RULE[code_coverage_and_quality_gates]>

<RULE[dynamic_metadata_and_observability]>
## 5. 动态元数据多级持久化与日志治理规范 (Metadata & Observability)
* **100% 数据库驱动与零硬编码字典**:
  - 严禁在前端或后端代码中硬编码任何业务字典（如 `OEXD` 费用代码、`OSLP` 销售员、`OCTG` 付款条件、`OHEM` 员工）；
  - 所有中文字段名与有效值下拉字典必须 100% 来源于 SAP 数据库 `CUFD` / `UFD1` / `OUDO` / `OUTB` 及系统字典表。
* **多级缓存与 10 分钟自动落盘调度**:
  - **第 1 级 (内存高速缓存)**: `ConcurrentDictionary`，0ms 极速响应；
  - **第 2 级 (本地磁盘持久化快照)**: 自动序列化落盘至 `metadata_cache/{CompanyId}_{ObjectCode}.json`，冷启动与离线秒级恢复；
  - **第 3 级 (后台定时静默同步)**: 后台守护服务采用 .NET 8 `PeriodicTimer`，**每 10 分钟自动全量同步并刷新落盘一次**；
  - **手动即时刷新**: 提供 `POST /api/v1/metadata/refresh` 供管理员实时触发生效。
* **全链路日志与异常治理**:
  - 严禁空 `catch` 吞没异常，所有降级分支必须记录结构化上下文日志；
  - 接入 Serilog 按天滚动物理日志文件，配置 `retainedFileCountLimit: 30` 自动清理 30 天前的旧日志；
  - 请求头贯穿 `X-Trace-Id` 并注入 Serilog `LogContext`。
</RULE[dynamic_metadata_and_observability]>

<RULE[local_vm_environment]>
## 6. 本地测试虚拟机连接信息 (Test Environment)
* **IP 地址**: 192.168.134.9
* **操作系统**: Windows Server 2025
* **系统管理员账号**: administrator
* **系统管理员密码**: 123456@aA
* **数据库版本**: SQL Server 2022
* **SA 账号**: sa
* **SA 密码**: 123456@a
* **SSH 免密直连**: `ssh -o StrictHostKeyChecking=no administrator@192.168.134.9 "<command>"`
</RULE[local_vm_environment]>

<RULE[devops_and_process_safety]>
## 7. 远程部署与执行防挂起红线 (Remote Execution & Process Safety)
* **杜绝 SSH 会话管道阻塞挂起**: 严禁通过交互式 SSH 执行启动 Windows 服务或可能阻塞 stdin/stdout 的脚本。数据库升级统一通过本地 TCP 直连执行，命令调用必须同步等待并保证句柄即时关闭。
* **文件同步性能标准**: 跨网络同步部署包时，严禁使用单线程慢速 `Copy-Item`，强制使用 Windows 原生多线程镜像增量工具 `robocopy /MIR`（秒级完成）。
* **进程主动收敛与 0 孤儿任务**: 每一轮工具调用与部署必须显式收敛，严禁在后台残留长期无响应的运行中任务。
</RULE[devops_and_process_safety]>

<RULE[enterprise_terminology_and_ux]>
## 8. 私有化企业级文案与极致 UX 规范 (Terminology & Fast-Path UX)
* **私有化服务器文案红线**: 本系统为企业局域网/私有化本地部署架构，全局前端与后端提示语严禁出现“云端/云端漫游”等公有云歧义词汇，必须严格规范为**“已保存到服务器”** / **“已同步至服务器”**。
* **0 延迟键盘盲操瞬切**: 待办工作台与单据详情必须保持内存高速缓存与零 DOM 颠簸（Zero Layout Thrashing），保障键盘 `J`/`K` 穿梭切换达到 0ms 电竞级丝滑响应。
</RULE[enterprise_terminology_and_ux]>

<RULE[frontend_devops_standards]>
## 9. 前端世界级 DevOps 与防白屏自动化质量门禁 (Frontend DevOps & Zero-White-Screen)
* **静态资产完整性哈希门禁 (Build Gate)**: 前端构建必须自动校验 `index.html` 引用的全部 JS/CSS/字体物理资产存在且非空，强力镜像同步至 `wwwroot`。
* **防缓存污染机制 (Zero-Stale Headers)**: Kestrel 服务端强制对 `index.html` 注入 `no-cache, no-store, must-revalidate`，带 Hash 资产注入 `max-age=31536000, immutable`，彻底消除客户端版本撕裂与旧缓存残留。
* **无头浏览器真机冒烟测试门禁 (Headless Smoke Gate)**: 任何部署脚本执行完毕后，必须由 CDP 无头浏览器自动加载页面并断言：
  1. `HTTP 404 / 500 资源请求数 === 0`；
  2. `JavaScript 运行时未捕获 Exception === 0`；
  3. `#app` 根节点挂载成功且 DOM 结构非空。
  若有任一项不达标，立即熔断阻断部署并报错。
</RULE[frontend_devops_standards]>
