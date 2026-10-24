import { createServer } from 'node:http';
import { realpath, stat } from 'node:fs/promises';
import path from 'node:path';

const responseHeaders = {
  'Cross-Origin-Opener-Policy': 'same-origin',
  'Cross-Origin-Embedder-Policy': 'require-corp',
  'Cross-Origin-Resource-Policy': 'same-origin',
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'no-referrer',
  'Content-Security-Policy': "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; worker-src 'self' blob:; style-src 'self' 'unsafe-inline'; connect-src 'self' blob: data:; img-src 'self' data: blob:; font-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'",
};

const mimeTypes = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.mjs', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.wasm', 'application/wasm'],
  ['.data', 'application/octet-stream'],
  ['.dll', 'application/octet-stream'],
  ['.dat', 'application/octet-stream'],
  ['.svg', 'image/svg+xml'],
  ['.png', 'image/png'],
  ['.jpg', 'image/jpeg'],
  ['.jpeg', 'image/jpeg'],
  ['.woff', 'font/woff'],
  ['.woff2', 'font/woff2'],
]);

function insideRoot(root, candidate) {
  return candidate === root || candidate.startsWith(`${root}${path.sep}`);
}

function sendText(response, status, body) {
  response.writeHead(status, { ...responseHeaders, 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store' });
  response.end(body);
}

function isRuntimeAsset(pathname) {
  return pathname.startsWith('/_framework/') || pathname.startsWith('/assets/');
}

async function resolveFile(root, pathname) {
  const decoded = decodeURIComponent(pathname);
  if (decoded.includes('\0') || decoded.includes('\\')) return { error: 400 };
  const candidate = path.resolve(root, `.${decoded}`);
  if (!insideRoot(root, candidate)) return { error: 403 };

  try {
    const resolved = await realpath(candidate);
    if (!insideRoot(root, resolved)) return { error: 403 };
    const metadata = await stat(resolved);
    if (!metadata.isFile()) return { error: 404 };
    return { path: resolved, size: metadata.size };
  } catch (error) {
    if (error && typeof error === 'object' && 'code' in error && error.code === 'ENOENT') return { error: 404 };
    throw error;
  }
}

export async function startStaticServer(directory) {
  const root = await realpath(directory);
  let expectedHost;
  const server = createServer(async (request, response) => {
    Object.entries(responseHeaders).forEach(([key, value]) => response.setHeader(key, value));
    if (request.headers.host !== expectedHost) {
      sendText(response, 400, 'Invalid host');
      return;
    }
    if (request.method !== 'GET' && request.method !== 'HEAD') {
      sendText(response, 405, 'Method not allowed');
      return;
    }
    if (!request.url?.startsWith('/')) {
      sendText(response, 400, 'Invalid request target');
      return;
    }

    let rawPath;
    try {
      rawPath = decodeURIComponent(request.url.split('?', 1)[0]);
    } catch {
      sendText(response, 400, 'Invalid request path');
      return;
    }
    if (rawPath.includes('\0') || rawPath.includes('\\') || rawPath.split('/').includes('..')) {
      sendText(response, 400, 'Invalid request path');
      return;
    }

    let pathname;
    try {
      pathname = new URL(request.url, 'http://127.0.0.1').pathname;
    } catch {
      sendText(response, 400, 'Invalid request target');
      return;
    }

    let resolved;
    try {
      resolved = await resolveFile(root, pathname);
    } catch {
      sendText(response, 400, 'Invalid request path');
      return;
    }

    if (resolved.error === 403) {
      sendText(response, 403, 'Forbidden');
      return;
    }
    if (resolved.error === 400) {
      sendText(response, 400, 'Invalid request path');
      return;
    }

    if (resolved.error === 404) {
      if (isRuntimeAsset(pathname)) {
        sendText(response, 404, 'Not found');
        return;
      }
      resolved = await resolveFile(root, '/index.html');
      if (resolved.error) {
        sendText(response, 404, 'Not found');
        return;
      }
    }

    const extension = path.extname(resolved.path).toLowerCase();
    response.statusCode = 200;
    response.setHeader('Content-Type', mimeTypes.get(extension) ?? 'application/octet-stream');
    response.setHeader('Content-Length', String(resolved.size));
    response.setHeader('Cache-Control', extension === '.html' || pathname.startsWith('/_framework/') ? 'no-store' : 'public, max-age=31536000, immutable');
    if (request.method === 'HEAD') {
      response.end();
      return;
    }
    const { createReadStream } = await import('node:fs');
    const stream = createReadStream(resolved.path);
    stream.on('error', () => {
      if (!response.headersSent) sendText(response, 500, 'Unable to read resource');
      else response.destroy();
    });
    stream.pipe(response);
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      server.removeListener('error', reject);
      resolve();
    });
  });
  const address = server.address();
  if (!address || typeof address === 'string') {
    server.close();
    throw new Error('Static server did not bind a TCP port.');
  }
  const url = `http://127.0.0.1:${address.port}/`;
  expectedHost = `127.0.0.1:${address.port}`;
  let closed = false;
  return {
    url,
    port: address.port,
    close: async () => {
      if (closed) return;
      closed = true;
      server.closeIdleConnections?.();
      server.closeAllConnections?.();
      await new Promise((resolve) => server.close(() => resolve()));
    },
  };
}
