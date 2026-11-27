import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { financeApi } from './server/api.js';

const here = path.dirname(fileURLToPath(import.meta.url));

// The hand-over folders and the seed data sit next to this app, under tracto/finance-poc/.
export default defineConfig({
  plugins: [
    react(),
    financeApi({
      dataDir: path.resolve(here, process.env.FINANCE_DATA_DIR || '../poc-data'),
      seedDir: path.resolve(here, process.env.FINANCE_SEED_DIR || '../seed'),
    }),
  ],
  server: {
    port: 5173,
    strictPort: true,
    // The data files change while the demo runs; they are not part of the app.
    watch: { ignored: ['**/poc-data/**'] },
  },
});
