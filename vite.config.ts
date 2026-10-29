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
    rolldownOptions: {
      input: { main: 'frontend/index.html', flood: 'frontend/flood.html' },
      output: {
        codeSplitting: {
          // Keep the 3D renderer lazy and cache its engine separately from the scene.
          groups: [
            { name: 'three-core', test: /node_modules\/three\/build\/three\.core\.js/, includeDependenciesRecursively: false },
            { name: 'three-renderer', test: /node_modules\/three\/build\/three\.module\.js/, includeDependenciesRecursively: false },
          ],
        },
      },
    },
    outDir: '../dist',
    emptyOutDir: true,
    target: 'es2022',
  },
}));
