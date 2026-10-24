import assert from 'node:assert/strict';
import { webcrypto, createHash } from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

// Contract check only: all gateway calls below use in-memory fixtures; no account, identity provider, or server is contacted.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-cumulus-auth-'));
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: {
      entry: path.join(root, 'frontend/src/features/editors/cumulusAuthentication.ts'),
      formats: ['es'],
      fileName: () => 'cumulusAuthentication.mjs',
    },
    outDir: temporary,
    emptyOutDir: true,
    minify: false,
    sourcemap: false,
  } });

  if (!globalThis.crypto?.subtle) {
    Object.defineProperty(globalThis, 'crypto', { configurable: true, value: webcrypto });
  }
  Object.defineProperty(globalThis, 'isSecureContext', { configurable: true, value: true });
  if (typeof globalThis.btoa !== 'function') {
    Object.defineProperty(globalThis, 'btoa', {
      configurable: true,
      value: (value) => Buffer.from(value, 'binary').toString('base64'),
    });
  }

  const {
    CumulusAuthenticationError,
    createCumulusAuthorizationAttempt,
    exchangeCumulusAuthorizationCode,
    parseCumulusCallbackUrl,
    refreshCumulusToken,
  } = await import(pathToFileURL(path.join(temporary, 'cumulusAuthentication.mjs')).href);

  const environment = {
    id: 'dev',
    name: 'CWBI Dev fixture',
    requiresSignIn: true,
    authorizeEndpoint: 'https://identity.example.test/realms/cwbi/protocol/openid-connect/auth',
    clientId: 'cumulus-fixture-client',
    redirectUri: 'http://127.0.0.1/callback',
    idpHint: 'federation-eams',
    mockAccessToken: null,
    isSelectable: true,
  };

  const first = await createCumulusAuthorizationAttempt(environment);
  const second = await createCumulusAuthorizationAttempt(environment);
  const authorizeUrl = new URL(first.authorizeUrl);
  assert.equal(authorizeUrl.origin, 'https://identity.example.test');
  assert.equal(authorizeUrl.pathname, '/realms/cwbi/protocol/openid-connect/auth');
  assert.equal(authorizeUrl.searchParams.get('client_id'), environment.clientId);
  assert.equal(authorizeUrl.searchParams.get('response_type'), 'code');
  assert.equal(authorizeUrl.searchParams.get('scope'), 'openid profile');
  assert.equal(authorizeUrl.searchParams.get('redirect_uri'), environment.redirectUri);
  assert.equal(authorizeUrl.searchParams.get('state'), first.state);
  assert.equal(authorizeUrl.searchParams.get('code_challenge_method'), 'S256');
  assert.equal(authorizeUrl.searchParams.get('kc-idp-hint'), environment.idpHint);
  assert.match(first.state, /^[A-Za-z0-9_-]{43}$/);
  assert.match(first.verifier, /^[A-Za-z0-9_-]{43}$/);
  assert.equal(authorizeUrl.searchParams.get('code_challenge'), createHash('sha256').update(first.verifier, 'ascii').digest('base64url'));
  assert.notEqual(first.state, second.state, 'Every sign-in attempt uses a fresh state value.');
  assert.notEqual(first.verifier, second.verifier, 'Every sign-in attempt uses a fresh PKCE verifier.');
  assert.notEqual(new URL(second.authorizeUrl).searchParams.get('code_challenge'), authorizeUrl.searchParams.get('code_challenge'));

  const callback = `http://127.0.0.1/callback?code=fixture-code&state=${encodeURIComponent(first.state)}&session_state=fixture-session`;
  assert.deepEqual(parseCumulusCallbackUrl(environment.redirectUri, callback, first.state), { code: 'fixture-code' });
  assert.throws(() => parseCumulusCallbackUrl(environment.redirectUri,
    `http://127.0.0.1/callback/extra?code=fixture-code&state=${first.state}`, first.state), CumulusAuthenticationError,
  'The callback path must exactly match the registered redirect.');
  assert.throws(() => parseCumulusCallbackUrl(environment.redirectUri,
    `http://localhost/callback?code=fixture-code&state=${first.state}`, first.state), CumulusAuthenticationError,
  'The callback host must exactly match the registered redirect.');
  assert.throws(() => parseCumulusCallbackUrl(environment.redirectUri,
    `https://127.0.0.1/callback?code=fixture-code&state=${first.state}`, first.state), CumulusAuthenticationError,
  'The callback scheme must exactly match the registered redirect.');
  assert.throws(() => parseCumulusCallbackUrl(environment.redirectUri,
    'http://127.0.0.1/callback?code=fixture-code&state=stale-state', first.state), CumulusAuthenticationError,
  'A mismatched state is rejected.');
  assert.throws(() => parseCumulusCallbackUrl(environment.redirectUri,
    `http://127.0.0.1/callback?code=one&code=two&state=${first.state}`, first.state), CumulusAuthenticationError,
  'Duplicate authorization codes are rejected.');
  assert.throws(() => parseCumulusCallbackUrl(environment.redirectUri,
    `http://127.0.0.1/callback?code=fixture-code&state=${first.state}&state=${first.state}`, first.state), CumulusAuthenticationError,
  'Duplicate state values are rejected.');

  const privateSentinel = 'fixture-secret-token-that-must-not-escape';
  let rejectedCallbackError;
  try {
    parseCumulusCallbackUrl(environment.redirectUri,
      `http://127.0.0.1/callback?error=access_denied&error_description=${encodeURIComponent(privateSentinel)}&state=${first.state}&access_token=${privateSentinel}`,
      first.state);
  } catch (error) {
    rejectedCallbackError = error;
  }
  assert.ok(rejectedCallbackError instanceof CumulusAuthenticationError, 'An OAuth error callback is rejected.');
  assert.equal(rejectedCallbackError.reason, 'authorizationRejected');
  assert.equal(rejectedCallbackError.localizedMessage('zh').includes(privateSentinel), false,
    'Provider error descriptions and token-like URL values are not included in user-facing errors.');
  assert.equal(JSON.stringify(rejectedCallbackError).includes(privateSentinel), false,
    'The rejected callback error does not serialize token-like values.');

  const tokenSet = {
    accessToken: 'fixture-access-token',
    refreshToken: 'fixture-refresh-token',
    accessExpiresUtc: '2030-01-01T00:00:00Z',
    refreshExpiresUtc: '2030-01-02T00:00:00Z',
  };
  const supported = new Set(['cumulus.token.exchange', 'cumulus.token.refresh']);
  const calls = [];
  const backend = {
    supports(method) { return supported.has(method); },
    async request(method, parameters, options) {
      calls.push({ method, parameters, options });
      return tokenSet;
    },
  };
  const signal = new AbortController().signal;
  const exchanged = await exchangeCumulusAuthorizationCode(backend, 'dev', 'fixture-authorization-code', 'fixture-pkce-verifier', signal);
  assert.deepEqual(exchanged, tokenSet);
  assert.equal(calls[0].method, 'cumulus.token.exchange');
  assert.deepEqual(calls[0].parameters, { environmentId: 'dev', code: 'fixture-authorization-code', verifier: 'fixture-pkce-verifier' });
  assert.strictEqual(calls[0].options.signal, signal);

  const refreshed = await refreshCumulusToken(backend, 'dev', 'fixture-refresh-token', signal);
  assert.deepEqual(refreshed, tokenSet);
  assert.equal(calls[1].method, 'cumulus.token.refresh');
  assert.deepEqual(calls[1].parameters, { environmentId: 'dev', refreshToken: 'fixture-refresh-token' });
  assert.strictEqual(calls[1].options.signal, signal);

  const invalidResponseBackend = {
    supports: () => true,
    async request() {
      return { accessToken: privateSentinel, refreshToken: null, accessExpiresUtc: 'not-a-date', refreshExpiresUtc: '2030-01-02T00:00:00Z' };
    },
  };
  await assert.rejects(exchangeCumulusAuthorizationCode(invalidResponseBackend, 'dev', 'fixture-code', 'fixture-verifier'), CumulusAuthenticationError,
    'Malformed exchange token responses fail closed.');
  await assert.rejects(refreshCumulusToken(invalidResponseBackend, 'dev', 'fixture-refresh-token'), CumulusAuthenticationError,
    'Malformed refresh token responses fail closed.');

  const unsupportedBackend = { supports: () => false, request: async () => tokenSet };
  await assert.rejects(exchangeCumulusAuthorizationCode(unsupportedBackend, 'dev', 'fixture-code', 'fixture-verifier'), CumulusAuthenticationError,
    'An unavailable Host exchange operation fails closed.');
  await assert.rejects(refreshCumulusToken(unsupportedBackend, 'dev', 'fixture-refresh-token'), CumulusAuthenticationError,
    'An unavailable Host refresh operation fails closed.');

  process.stdout.write(JSON.stringify({
    state: 'passed',
    checks: 38,
    scope: 'cumulus-pkce-callback-and-recorded-host-contract',
    gateway: 'in-memory-mock-only',
    liveAccountSignIn: false,
    serverContacted: false,
  }) + '\n');
} finally {
  await rm(temporary, { recursive: true, force: true });
}
