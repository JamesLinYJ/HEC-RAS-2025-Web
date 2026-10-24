import type { BackendGateway, Language } from '../../shared/workbench';
import type { CumulusEnvironmentOption, CumulusTokenSet } from './cumulusTypes';

const exchangeOperation = 'cumulus.token.exchange';
const refreshOperation = 'cumulus.token.refresh';

export type CumulusAuthenticationFailure =
  | 'unavailableEnvironment'
  | 'unavailableAuthorization'
  | 'secureContextRequired'
  | 'invalidRedirect'
  | 'invalidCallback'
  | 'stateMismatch'
  | 'authorizationRejected'
  | 'missingCode'
  | 'invalidTokenResponse'
  | 'operationUnavailable';

export class CumulusAuthenticationError extends Error {
  constructor(readonly reason: CumulusAuthenticationFailure) {
    super(reason);
    this.name = 'CumulusAuthenticationError';
  }

  localizedMessage(language: Language): string {
    const labels: Record<CumulusAuthenticationFailure, [string, string]> = {
      unavailableEnvironment: ['此 Cumulus 环境不可选择。', 'This Cumulus environment cannot be selected.'],
      unavailableAuthorization: ['所选环境没有可用的真实登录配置。', 'The selected environment has no usable sign-in configuration.'],
      secureContextRequired: ['浏览器安全上下文不支持 PKCE。请通过 HTTPS、localhost 或桌面应用重试。', 'This browser context does not support PKCE. Retry over HTTPS, localhost, or the desktop app.'],
      invalidRedirect: ['服务端返回的 Cumulus 回调地址无效。', 'The server returned an invalid Cumulus redirect URI.'],
      invalidCallback: ['回调地址与本次登录配置不匹配，或缺少必需参数。请重新开始登录。', 'The callback URL does not match this sign-in attempt or is missing required values. Start sign-in again.'],
      stateMismatch: ['登录状态校验失败。为保护账户，本次授权已丢弃，请重新登录。', 'The sign-in state did not match. This authorization was discarded; sign in again.'],
      authorizationRejected: ['Cumulus 身份服务拒绝或取消了登录。请重新开始登录。', 'The Cumulus identity provider rejected or cancelled sign-in. Start again.'],
      missingCode: ['回调地址中没有授权码。请重新开始登录。', 'The callback URL did not contain an authorization code. Start again.'],
      invalidTokenResponse: ['Cumulus 服务返回了无效的令牌响应。', 'Cumulus returned an invalid token response.'],
      operationUnavailable: ['当前 C# 服务未发布所需的 Cumulus 登录操作。', 'The current C# service does not advertise the required Cumulus sign-in operation.'],
    };
    const pair = labels[this.reason];
    return pair[language === 'en' ? 1 : 0];
  }
}

export interface CumulusAuthorizationAttempt {
  authorizeUrl: string;
  state: string;
  verifier: string;
  environmentId: string;
}

/** Creates the same Authorization Code + PKCE S256 request used by OidcClient. */
export async function createCumulusAuthorizationAttempt(
  environment: CumulusEnvironmentOption,
): Promise<CumulusAuthorizationAttempt> {
  if (!environment.isSelectable) throw new CumulusAuthenticationError('unavailableEnvironment');
  if (!environment.authorizeEndpoint || !environment.clientId || !environment.redirectUri || !environment.requiresSignIn) {
    throw new CumulusAuthenticationError('unavailableAuthorization');
  }
  if (!globalThis.isSecureContext || !globalThis.crypto?.getRandomValues || !globalThis.crypto.subtle) {
    throw new CumulusAuthenticationError('secureContextRequired');
  }

  const redirect = parseRedirectUri(environment.redirectUri);
  const authorizeEndpoint = parseAuthorizeEndpoint(environment.authorizeEndpoint);
  const verifier = randomUrlToken(32);
  const state = randomUrlToken(32);
  const challengeBytes = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  const challenge = encodeBase64Url(new Uint8Array(challengeBytes));

  const authorizeUrl = new URL(authorizeEndpoint.href);
  authorizeUrl.searchParams.set('client_id', environment.clientId);
  authorizeUrl.searchParams.set('response_type', 'code');
  authorizeUrl.searchParams.set('scope', 'openid profile');
  authorizeUrl.searchParams.set('redirect_uri', redirect.href);
  authorizeUrl.searchParams.set('state', state);
  authorizeUrl.searchParams.set('code_challenge', challenge);
  authorizeUrl.searchParams.set('code_challenge_method', 'S256');
  if (environment.idpHint) authorizeUrl.searchParams.set('kc-idp-hint', environment.idpHint);

  return { authorizeUrl: authorizeUrl.href, state, verifier, environmentId: environment.id };
}

/** Strictly accepts only the registered redirect origin/path and this attempt's state. */
export function parseCumulusCallbackUrl(
  redirectUri: string,
  pastedCallbackUrl: string,
  expectedState: string,
): { code: string } {
  const expected = parseRedirectUri(redirectUri);
  let actual: URL;
  try {
    actual = new URL(pastedCallbackUrl.trim());
  } catch {
    throw new CumulusAuthenticationError('invalidCallback');
  }
  if (actual.origin !== expected.origin || actual.pathname !== expected.pathname || actual.hash || actual.username || actual.password) {
    throw new CumulusAuthenticationError('invalidCallback');
  }

  const states = actual.searchParams.getAll('state');
  if (states.length !== 1 || states[0] !== expectedState) {
    throw new CumulusAuthenticationError('stateMismatch');
  }

  const errors = actual.searchParams.getAll('error');
  if (errors.length > 1) throw new CumulusAuthenticationError('invalidCallback');
  if (errors.length === 1 && errors[0]) throw new CumulusAuthenticationError('authorizationRejected');

  const codes = actual.searchParams.getAll('code');
  if (codes.length !== 1 || !codes[0]?.trim()) {
    throw new CumulusAuthenticationError(codes.length === 0 ? 'missingCode' : 'invalidCallback');
  }
  return { code: codes[0] };
}

export async function exchangeCumulusAuthorizationCode(
  backend: BackendGateway,
  environmentId: string,
  code: string,
  verifier: string,
  signal?: AbortSignal,
): Promise<CumulusTokenSet> {
  if (!backend.supports(exchangeOperation)) throw new CumulusAuthenticationError('operationUnavailable');
  const response = await backend.request<CumulusTokenSet>(exchangeOperation, { environmentId, code, verifier }, { signal });
  return validateTokenSet(response);
}

export async function refreshCumulusToken(
  backend: BackendGateway,
  environmentId: string,
  refreshToken: string,
  signal?: AbortSignal,
): Promise<CumulusTokenSet> {
  if (!backend.supports(refreshOperation)) throw new CumulusAuthenticationError('operationUnavailable');
  const response = await backend.request<CumulusTokenSet>(refreshOperation, { environmentId, refreshToken }, { signal });
  return validateTokenSet(response);
}

function validateTokenSet(value: unknown): CumulusTokenSet {
  if (!value || typeof value !== 'object') throw new CumulusAuthenticationError('invalidTokenResponse');
  const candidate = value as Partial<CumulusTokenSet>;
  if (typeof candidate.accessToken !== 'string' || !candidate.accessToken.trim() ||
      !(candidate.refreshToken === null || typeof candidate.refreshToken === 'string') ||
      typeof candidate.accessExpiresUtc !== 'string' || !Number.isFinite(Date.parse(candidate.accessExpiresUtc)) ||
      typeof candidate.refreshExpiresUtc !== 'string' || !Number.isFinite(Date.parse(candidate.refreshExpiresUtc))) {
    throw new CumulusAuthenticationError('invalidTokenResponse');
  }
  return candidate as CumulusTokenSet;
}

function parseRedirectUri(value: string): URL {
  let uri: URL;
  try {
    uri = new URL(value);
  } catch {
    throw new CumulusAuthenticationError('invalidRedirect');
  }
  const loopback = uri.hostname === 'localhost' || uri.hostname === '127.0.0.1' || uri.hostname === '[::1]';
  if ((uri.protocol !== 'https:' && !(uri.protocol === 'http:' && loopback)) || uri.username || uri.password || uri.search || uri.hash) {
    throw new CumulusAuthenticationError('invalidRedirect');
  }
  return uri;
}

function parseAuthorizeEndpoint(value: string): URL {
  let uri: URL;
  try {
    uri = new URL(value);
  } catch {
    throw new CumulusAuthenticationError('unavailableAuthorization');
  }
  if (uri.protocol !== 'https:' || uri.username || uri.password) {
    throw new CumulusAuthenticationError('unavailableAuthorization');
  }
  return uri;
}

function randomUrlToken(byteCount: number): string {
  const bytes = globalThis.crypto.getRandomValues(new Uint8Array(byteCount));
  return encodeBase64Url(bytes);
}

function encodeBase64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}
