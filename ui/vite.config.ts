import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// O console é servido pelo próprio gateway (mesma origem da API, sem CORS):
// - `npm run build` escreve direto no wwwroot do gateway — `dotnet run` já serve a UI;
// - `npm run dev` (Vite em :5173) repassa /api ao gateway rodando em :5080.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/SatusBlockchain.Gateway/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5080' },
  },
  test: {
    environment: 'jsdom',
  },
});
