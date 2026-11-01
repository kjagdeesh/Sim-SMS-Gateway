const http = require("node:http");
const { readFile, readdir } = require("node:fs/promises");
const path = require("node:path");

const root = __dirname;
const port = Number(process.env.PORT ?? 8766);
const mimeTypes = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".woff2": "font/woff2",
  ".txt": "text/plain; charset=utf-8"
};
const excluded = new Set(["server.js", "package.json", "package-lock.json"]);

// Only files inside the web folder with a known type are served; node_modules and server code are not.
async function collectFiles(directory, prefix = "") {
  const files = new Map();
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.name === "node_modules" || entry.name.startsWith(".")) continue;
    const relative = `${prefix}/${entry.name}`;
    if (entry.isDirectory()) {
      for (const [key, value] of await collectFiles(path.join(directory, entry.name), relative)) files.set(key, value);
    } else if (!excluded.has(entry.name) && mimeTypes[path.extname(entry.name)]) {
      files.set(relative, [path.join(directory, entry.name), mimeTypes[path.extname(entry.name)]]);
    }
  }
  return files;
}

const csp = "default-src 'self'; connect-src http: https:; img-src 'self' https://avatars.githubusercontent.com; style-src 'self'; script-src 'self'; base-uri 'none'; frame-ancestors 'none'";

async function main() {
  const files = await collectFiles(root);
  files.set("/", files.get("/index.html"));

  const server = http.createServer(async (request, response) => {
    if (request.method !== "GET") {
      response.writeHead(405, { "Content-Type": "text/plain; charset=utf-8", "Allow": "GET" });
      response.end("Method not allowed");
      return;
    }

    const pathname = decodeURIComponent(new URL(request.url, `http://${request.headers.host}`).pathname);
    const file = files.get(pathname);
    if (!file) {
      response.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
      response.end("Not found");
      return;
    }

    try {
      const content = await readFile(file[0]);
      response.writeHead(200, {
        "Content-Type": file[1],
        "Content-Length": content.length,
        "X-Content-Type-Options": "nosniff",
        "Content-Security-Policy": csp
      });
      response.end(content);
    } catch (error) {
      console.error("Dashboard asset read failed:", error);
      response.writeHead(500, { "Content-Type": "text/plain; charset=utf-8" });
      response.end("Could not read the dashboard asset.");
    }
  });

  server.listen(port, "127.0.0.1", () => {
    console.log(`Sim SMS Gateway dashboard: http://localhost:${port}`);
  });
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
