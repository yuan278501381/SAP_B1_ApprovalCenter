# Approval.Web

Vue 3 审批工作台。开发模式通过 Vite 代理访问 `http://localhost:5000/api`，并显示用户模拟器；生产构建使用同源 `/api/v1`，用户身份必须由反向代理认证后注入。

```powershell
npm ci
npm run dev
npm run build
```

如 API 不与网页同源，可在构建时设置 `VITE_API_BASE`。生产 CORS 必须同步加入该站点的精确 Origin，不能使用通配符。
