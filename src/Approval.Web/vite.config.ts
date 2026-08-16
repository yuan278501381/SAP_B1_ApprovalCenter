import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  server: {
    host: true, // 监听 0.0.0.0，支持宿主机、局域网及测试虚拟机(192.168.134.9)直接访问
    strictPort: false, // 端口被占用时自动寻找下一个可用端口 (如 5173 -> 5174 -> 5175 等)
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    rollupOptions: {
      output: {
        manualChunks(id: string) {
          if (id.includes('node_modules/vue') || id.includes('node_modules/@vue') || id.includes('node_modules/vue-router')) {
            return 'vendor-vue'
          }
          if (id.includes('node_modules/bpmn-js')) {
            return 'vendor-bpmn'
          }
          if (id.includes('node_modules/@logicflow')) {
            return 'vendor-logicflow'
          }
          if (id.includes('node_modules/axios')) {
            return 'vendor-axios'
          }
          if (id.includes('node_modules/lucide-vue-next')) {
            return 'vendor-lucide'
          }
        }
      }
    },
    chunkSizeWarningLimit: 600
  }
})
