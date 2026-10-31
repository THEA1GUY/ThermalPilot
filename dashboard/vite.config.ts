import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  css: { postcss: { plugins: [] } },
  build: {
    outDir: "../hub/ThermalPilot.Host/wwwroot",
    emptyOutDir: true,
    sourcemap: false,
  },
});
