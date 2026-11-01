const http = require("node:http");
const { readFile } = require("node:fs/promises");
const path = require("node:path");

const root = __dirname;
const port = Number(process.env.PORT ?? 8766);
const files = new Map([
  ["/", ["index.html", "text/html; charset=utf-8"]],
  ["/index.html", ["index.html", "text/html; charset=utf-8"]],
  ["/favicon.svg", ["favicon.svg", "image/svg+xml"]],
  ["/styles.css", ["styles.css", "text/css; charset=utf-8"]],
  ["/app.js", ["app.js", "text/javascript; charset=utf-8"]]
]);

const server = http.createServer(async (request, response) => {
  if (request.method !== "GET") {
    response.writeHead(405, { "Content-Type": "text/plain; charset=utf-8", "Allow": "GET" });
    response.end("Method not allowed");
    return;
  }

  const pathname = new URL(request.url, `http://${request.headers.host}`).pathname;
  const file = files.get(pathname);
  if (!file) {
    response.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    response.end("Not found");
    return;
  }

  try {
    const content = await readFile(path.join(root, file[0]));
    response.writeHead(200, {
      "Content-Type": file[1],
      "Content-Length": content.length,
      "X-Content-Type-Options": "nosniff",
      "Content-Security-Policy": "default-src 'self'; connect-src http: https:; style-src 'self'; script-src 'self'; base-uri 'none'; frame-ancestors 'none'"
    });
    response.end(content);
  } catch (error) {
    console.error("Playground asset read failed:", error);
    response.writeHead(500, { "Content-Type": "text/plain; charset=utf-8" });
    response.end("Could not read the playground asset.");
  }
});

server.listen(port, "127.0.0.1", () => {
  console.log(`SIM Relay playground: http://localhost:${port}`);
});
