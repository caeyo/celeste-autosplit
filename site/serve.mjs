// Serves the site locally the same way it is deployed: site/ plus a copy of the
// shared settings schema (see .github/workflows/site.yml). No dependencies.
//
//   node site/serve.mjs [port]

import { copyFileSync, createReadStream, statSync } from "node:fs";
import { createServer } from "node:http";
import { extname, join, normalize, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL(".", import.meta.url));
const port = Number(process.argv[2] ?? 8000);

copyFileSync(join(root, "../schema/settings-schema.json"), join(root, "settings-schema.json"));

const types = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
};

createServer((req, res) => {
  let path = normalize(decodeURIComponent(new URL(req.url, "http://x").pathname));
  if (path.endsWith("/") || path.endsWith("\\")) path = join(path, "index.html");
  const file = resolve(root, "." + path);
  try {
    if (!file.startsWith(resolve(root)) || !statSync(file).isFile()) throw 0;
  } catch {
    res.writeHead(404).end("Not found");
    return;
  }
  res.writeHead(200, { "Content-Type": types[extname(file)] ?? "application/octet-stream" });
  createReadStream(file).pipe(res);
}).listen(port, () => console.log(`Serving site at http://localhost:${port}/`));
