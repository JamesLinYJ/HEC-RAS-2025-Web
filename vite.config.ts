import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => ({
  plugins: [react()],
  publicDir: 'public',
  root: 'frontend',
  base: './',
  server: {
    host: '127.0.0.1',
    proxy: {
      '/api': {
        target: loadEnv(mode, '.', 'HECRAS_').HECRAS_SERVER_URL || 'http://127.0.0.1:5080',
        changeOrigin: true,
      },
    },
  },
  preview: {
    host: '127.0.0.1',
  },
  build: {
    outDir: '../dist',
    emptyOutDir: true,
    target: 'es2022',
  },
}));
