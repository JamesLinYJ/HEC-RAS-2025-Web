import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const externalServerConfigured = Boolean(process.env.HECRAS_SERVER_URL?.trim());

function run(executable, args, label) {
  return new Promise((resolve, reject) => {
    const child = spawn(executable, args, {
      cwd: root,
      env: process.env,
      stdio: 'inherit',
      shell: false,
      windowsHide: true,
    });
    const forward = (signal) => child.kill(signal);
    process.once('SIGINT', forward);
    process.once('SIGTERM', forward);
    child.once('error', (error) => {
      process.removeListener('SIGINT', forward);
      process.removeListener('SIGTERM', forward);
      reject(new Error(`Could not start ${label}: ${error.message}`));
    });
    child.once('exit', (code, signal) => {
      process.removeListener('SIGINT', forward);
      process.removeListener('SIGTERM', forward);
      resolve(signal ? 128 : code ?? 1);
    });
  });
}

try {
  if (!externalServerConfigured) {
    const buildCode = await run(process.execPath, [path.join(root, 'scripts/build-server.mjs')], 'the C# server publish');
    if (buildCode !== 0) process.exitCode = buildCode;
  }

  if (!process.exitCode) {
    const electronCli = path.join(root, 'node_modules/electron/cli.js');
    const electronCode = await run(process.execPath, [electronCli, path.join(here, 'main.mjs'), ...process.argv.slice(2)], 'Electron');
    if (electronCode !== 0) process.exitCode = electronCode;
  }
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
}
