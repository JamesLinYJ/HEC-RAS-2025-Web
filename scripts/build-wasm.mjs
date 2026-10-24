import { spawn } from 'node:child_process';
import { access, cp, mkdir, mkdtemp, rename, rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dotnet = process.env.HECRAS_DOTNET ?? 'dotnet';
const artifacts = path.join(root, 'artifacts');
await mkdir(artifacts, { recursive: true });
const output = await mkdtemp(path.join(artifacts, 'wasm-publish-'));
const publicRoot = path.join(root, 'frontend/public');
await mkdir(publicRoot, { recursive: true });
const destination = path.join(publicRoot, '_framework');
const stage = path.join(publicRoot, `_framework-stage-${randomUUID()}`);
const previous = path.join(publicRoot, `_framework-previous-${randomUUID()}`);
let hadPrevious = false;
try {
  const child = spawn(dotnet, ['publish', 'backend/Wasm/Ras.Wasm.csproj', '-c', 'Release', '-o', output, '-warnaserror', '--nologo'], {
    cwd: root, env: process.env, stdio: 'inherit', shell: false,
  });
  const exitCode = await new Promise((resolve, reject) => {
    child.once('error', reject); child.once('exit', (code) => resolve(code ?? 1));
  });
  if (exitCode !== 0) {
    process.exitCode = exitCode;
    throw new Error(`C# WebAssembly publish failed with exit code ${exitCode}.`);
  }
  const framework = path.join(output, 'wwwroot/_framework');
  await access(path.join(framework, 'dotnet.js'));
  await cp(framework, stage, { recursive: true });
  try { await access(destination); hadPrevious = true; } catch (error) { if (error.code !== 'ENOENT') throw error; }
  if (hadPrevious) await rename(destination, previous);
  try { await rename(stage, destination); }
  catch (error) { if (hadPrevious) await rename(previous, destination); throw error; }
  await rm(previous, { recursive: true, force: true });
} finally {
  await rm(stage, { recursive: true, force: true });
  await rm(output, { recursive: true, force: true });
}
process.stdout.write('Published C# WebAssembly resources for the shared Web/Electron frontend.\n');
