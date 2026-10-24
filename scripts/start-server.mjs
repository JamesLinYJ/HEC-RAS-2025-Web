import { access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { connectBackend } from '../desktop/server-runtime.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const webRoot = path.resolve(process.env.HECRAS_WEB_ROOT ?? path.join(root, 'dist'));
let connection;
let keepAlive;
let stopping;
let terminationSignal;
const startupAbort = new AbortController();

function interruptStartup(signal) {
  terminationSignal ??= signal;
  startupAbort.abort();
  if (connection) void stop();
}

process.once('SIGINT', () => interruptStartup('SIGINT'));
process.once('SIGTERM', () => interruptStartup('SIGTERM'));

async function stop() {
  if (stopping) return stopping;
  stopping = (async () => {
    if (keepAlive) clearInterval(keepAlive);
    try {
      await connection?.close();
    } catch (error) {
      process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
      process.exitCode = 1;
    }
    if (!process.exitCode) process.exitCode = terminationSignal === 'SIGINT' ? 130 : 0;
    process.exit(process.exitCode);
  })();
  return stopping;
}

try {
  if (!process.env.HECRAS_SERVER_URL?.trim()) await access(path.join(webRoot, 'index.html'));
  connection = await connectBackend({ projectRoot: root, webRoot, signal: startupAbort.signal });
  process.stdout.write(`HEC-RAS server ready at ${connection.baseUrl}/ (${connection.ownership}).\n`);
  keepAlive = setInterval(() => {}, 60_000);
} catch (error) {
  if (!terminationSignal) process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  await connection?.close().catch(() => {});
  process.exitCode = terminationSignal === 'SIGINT' ? 130 : terminationSignal === 'SIGTERM' ? 143 : 1;
}
