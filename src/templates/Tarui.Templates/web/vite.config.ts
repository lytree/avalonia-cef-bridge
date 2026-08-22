import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Defaults follow the roadmap: the dev server binds localhost:5173, which matches
// the scaffolded tarui.app.json build.devUrl used by `tarui dev`.
export default defineConfig({
  plugins: [react()],
})