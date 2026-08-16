import axios from 'axios'
import type { AxiosInstance, AxiosError } from 'axios'
import { appConfig } from './index'

const API_BASE = import.meta.env.VITE_API_BASE || '/api/v1'

// 全局单例 API 客户端 —— 统一拦截、错误处理与 TraceID 注入
const api: AxiosInstance = axios.create({
  baseURL: API_BASE,
  timeout: 30000
})

// 请求拦截器：自动注入操作员与链路追踪 ID
api.interceptors.request.use((config) => {
  const user = localStorage.getItem('sap_b1_approval_user') || 'manager'
  config.headers['X-Approval-User'] = user
  config.headers['X-Approval-User-Name'] = user
  if (!config.headers['X-Trace-Id']) {
    config.headers['X-Trace-Id'] = 'trace_fe_' + Math.random().toString(36).substring(2, 9)
  }
  return config
})

// 响应拦截器：统一错误处理与降级
api.interceptors.response.use(
  (response) => response,
  (error: AxiosError) => {
    const status = error.response?.status
    if (status === 401) {
      console.error('[API] 认证失败，请重新登录')
    } else if (status === 500) {
      console.error('[API] 服务器内部错误')
    }
    return Promise.reject(error)
  }
)

export default api
export { API_BASE }
