import { spawn } from 'node:child_process';
import { access, mkdir, mkdtemp, rename, rm, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const artifacts = path.join(root, 'artifacts');
const project = path.join(root, 'backend/Server/Ras.Backend.Server.csproj');
const packages = path.join(root, 'backend/Packages');
const destination = path.join(artifacts, 'server');
const dotnet = process.env.HECRAS_DOTNET?.trim() || 'dotnet';
const platform = { darwin: 'osx', linux: 'linux', win32: 'win' }[process.platform];
const architecture = { arm64: 'arm64', x64: 'x64', ia32: 'x86' }[process.arch];
const runtime = process.env.HECRAS_RUNTIME_IDENTIFIER?.trim() || (platform && architecture ? `${platform}-${architecture}` : '');
if (!/^(osx|linux|win)-(arm64|x64|x86)$/.test(runtime))
  throw new Error('Set HECRAS_RUNTIME_IDENTIFIER to the target server runtime identifier.');
await mkdir(artifacts, { recursive: true });
const stage = await mkdtemp(path.join(artifacts, 'server-publish-'));
const previous = path.join(artifacts, `server-previous-${process.pid}-${Date.now()}`);
let child;
let signalReceived;
let retainPreviousBackup = false;

function forwardSignal(signal) {
  signalReceived ??= signal;
  child?.kill(signal);
}

const onInterrupt = () => forwardSignal('SIGINT');
const onTerminate = () => forwardSignal('SIGTERM');
process.once('SIGINT', onInterrupt);
process.once('SIGTERM', onTerminate);

async function publish() {
  const sources = [packages];
  const configuredSource = process.env.HECRAS_NUGET_SOURCE?.trim();
  if (configuredSource) {
    let source;
    if (/^[a-z]:[\\/]/i.test(configuredSource) || !/^[a-z][a-z\d+.-]*:/i.test(configuredSource)) {
      source = path.resolve(root, configuredSource);
    } else {
      try {
        const parsed = new URL(configuredSource);
        if (parsed.protocol === 'file:' && !parsed.username && !parsed.password && !parsed.search && !parsed.hash) source = fileURLToPath(parsed);
        else if (['http:', 'https:'].includes(parsed.protocol) && !parsed.username && !parsed.password && !parsed.search && !parsed.hash) source = parsed.href;
        else throw new Error('invalid feed');
      } catch {
        throw new Error('HECRAS_NUGET_SOURCE must be a local path or HTTP(S) NuGet feed without URL credentials, a query, or a fragment.');
      }
    }
    sources.push(source);
  }
  const args = [
    'publish', project,
    '--configuration', 'Release',
    '--runtime', runtime,
    '--self-contained', 'false',
    '--output', stage,
    '-warnaserror',
    '--nologo',
    `-p:RestoreAdditionalProjectSources=${sources.join(';')}`,
  ];
  if (process.env.HECRAS_NO_RESTORE === '1') args.push('--no-restore');
  child = spawn(dotnet, args, {
    cwd: root,
    env: process.env,
    stdio: 'inherit',
    shell: false,
    windowsHide: true,
  });
  if (signalReceived) child.kill(signalReceived);
  const exitCode = await new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('exit', (code, signal) => resolve(signal ? 128 : code ?? 1));
  });
  if (signalReceived) {
    process.exitCode = signalReceived === 'SIGINT' ? 130 : 143;
    throw new Error('Server publish was interrupted.');
  }
  if (exitCode !== 0) throw new Error(`C# server publish failed with exit code ${exitCode}.`);

  const required = [
    'Ras.Backend.Server.dll',
    'Ras.Backend.Server.deps.json',
    'Ras.Backend.Server.runtimeconfig.json',
  ];
  for (const name of required) {
    const file = path.join(stage, name);
    await access(file);
    if ((await stat(file)).size === 0) throw new Error(`C# publish produced an empty ${name}.`);
  }

  let hadPrevious = false;
  try {
    await access(destination);
    hadPrevious = true;
  } catch (error) {
    if (error?.code !== 'ENOENT') throw error;
  }

  let previousBackedUp = false;
  if (hadPrevious) {
    await rename(destination, previous);
    previousBackedUp = true;
  }
  try {
    await rename(stage, destination);
  } catch (error) {
    if (previousBackedUp) {
      try {
        await rename(previous, destination);
        previousBackedUp = false;
      } catch (restoreError) {
        retainPreviousBackup = true;
        throw new Error(`Publish replacement failed and the previous server remains at ${previous}: ${restoreError instanceof Error ? restoreError.message : String(restoreError)}`);
      }
    }
    throw error;
  }
  if (previousBackedUp) {
    await rm(previous, { recursive: true, force: true });
    previousBackedUp = false;
  }
}

try {
  await publish();
  process.stdout.write(`Published the C# server to ${destination}.\n`);
} catch (error) {
  if (!process.exitCode) process.exitCode = 1;
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
} finally {
  process.removeListener('SIGINT', onInterrupt);
  process.removeListener('SIGTERM', onTerminate);
  await rm(stage, { recursive: true, force: true });
  if (!retainPreviousBackup) await rm(previous, { recursive: true, force: true });
}
