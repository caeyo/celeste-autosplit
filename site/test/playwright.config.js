import { defineConfig } from "@playwright/test";

const port = 8123;

export default defineConfig({
  testMatch: "site.spec.js",
  forbidOnly: true,
  reporter: "list",
  timeout: 60000,
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    browserName: "chromium",
    headless: true,
    acceptDownloads: true,
  },
  webServer: {
    command: `node ../serve.mjs ${port}`,
    url: `http://127.0.0.1:${port}/`,
    reuseExistingServer: false,
  },
});
