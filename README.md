# SAP B1 通用审批平台

这是基于既定架构继续完成的第一条可靠纵向切片，适配 SAP Business One 10.0、SQL Server 和“辅助平台无源码”的现场约束。

## 当前真实能力

- `CHORDR`、`CHOQUT` 通过统一 Adapter 接入；开发环境使用 Fake，生产可切换 Service Layer。
- 已发布流程版本 + 公司/对象绑定；支持 `DocTotal` 条件和串行人工审批。
- 支持同意、拒绝、退回。退回会结束当前实例，修改后可重新提交。
- 规范化 JSON 快照、SHA-256、操作日志、Trace ID。
- 写操作强制 `Idempotency-Key`；SQL 事务、运行实例唯一索引、`rowversion` 并发保护。
- Outbox 重试、崩溃后的 Processing 租约恢复、Worker 抢占并发保护及 SAP 同步状态。
- Vue 工作台展示待办、单据快照和轨迹；开发模式可模拟用户，生产模式只接受统一认证。

## 当前明确不支持

- 并行网关、会签/N-M 签、加签、转交、委托、角色/主管组织树解析。
- BPMN 2.0 设计器和任意表达式脚本；这些节点目前会明确报错，不会静默错误执行。
- 邮件、企业微信、钉钉、Teams 等通知渠道。
- 辅助平台 Web 控件中的网页直接控制 SAP UI。仍须使用既有黄箭头/带链接控件打开 SAP 窗口。
- Service Layer 的现场 EntitySet、键类型、字段名尚未用 `DB_KCC` 实测；示例配置不能直接视为生产配置。
- 仅有审批镜像不等于业务封锁。下游生成订单/投产/出库必须查询审批状态，或在 `SBO_SP_TransactionNotification` 中设置服务端门禁。

详细边界与下一步见 [当前实现状态与生产落地清单](docs/02-当前实现状态与生产落地清单.md)。

## 本地验证

```powershell
dotnet test .\ApprovalPlatform.sln
Set-Location .\src\Approval.Web
npm ci
npm run build
```

API 的 Development 配置使用独立内存库与 Fake Adapter。启动 API 和前端：

```powershell
dotnet run --project .\src\Approval.Api
Set-Location .\src\Approval.Web
npm run dev
```

生产部署必须先执行 [ApprovalDB 初始化脚本](database/01_init_approval_db.sql)，并以 [Service Layer 配置示例](deploy/appsettings.ServiceLayer.example.json) 为模板通过环境变量或 Secret Store 提供密码。旧版数据库再执行 [可靠性升级脚本](database/02_upgrade_reliability.sql)。

## 安全约束

API 使用 `X-Approval-User`/`X-Approval-User-Name` 作为“受信任网关注入头”，并在非 Development 环境校验 `X-Approval-Gateway-Secret`。生产中必须：

1. 禁止用户网络直接访问 API；
2. 由反向代理完成登录或 SSO；
3. 代理先删除客户端传入的同名头，再注入认证后的身份和网关共享密钥；
4. CORS 只配置审批站点白名单；
5. 网关密钥和 SAP Service Layer 凭据只能由环境变量或 Secret Store 提供，不得提交到仓库。

否则这些请求头可以被伪造，不能视为可靠身份认证。
