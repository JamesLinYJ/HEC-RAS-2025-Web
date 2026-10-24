import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import type { BackendGateway, Language } from '../../shared/workbench';
import { message } from '../../shared/workbench';
import type { CumulusEnvironmentOption, CumulusSessionToken, CumulusTokenSet } from './cumulusTypes';
import {
  CumulusAuthenticationError,
  createCumulusAuthorizationAttempt,
  exchangeCumulusAuthorizationCode,
  parseCumulusCallbackUrl,
  refreshCumulusToken,
} from './cumulusAuthentication';
import './cumulus-login.css';

export interface CumulusLoginWindowProps {
  environment: CumulusEnvironmentOption;
  backend: BackendGateway;
  busy?: boolean;
  language?: Language;
  onAuthenticated: (token: CumulusSessionToken) => void;
  onError: (message: string) => void;
  onSignedOut?: () => void;
}

interface PendingLogin {
  state: string;
  verifier: string;
  environmentId: string;
}

/** Browser/Electron counterpart to CumulusLoginWindow; credentials are kept in memory only. */
export function CumulusLoginWindow(props: CumulusLoginWindowProps) {
  const language = props.language ?? 'zh';
  const label = (zh: string, en: string) => language === 'en' ? en : zh;
  const [pendingCallback, setPendingCallback] = useState(false);
  const [callbackUrl, setCallbackUrl] = useState('');
  const [revealCallbackUrl, setRevealCallbackUrl] = useState(false);
  const [session, setSession] = useState<CumulusSessionToken | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const pendingRef = useRef<PendingLogin | null>(null);
  const popupRef = useRef<Window | null>(null);
  const controllerRef = useRef<AbortController | null>(null);
  const generationRef = useRef(0);
  const environmentIdRef = useRef(props.environment.id);
  const onAuthenticatedRef = useRef(props.onAuthenticated);
  const onErrorRef = useRef(props.onError);
  const onSignedOutRef = useRef(props.onSignedOut);
  const parentBusy = props.busy === true;
  const interactionBusy = busy || parentBusy;
  const canUseMock = props.environment.isSelectable && !!props.environment.mockAccessToken && !props.environment.requiresSignIn;
  const canStartSignIn = props.environment.isSelectable && props.environment.requiresSignIn &&
    !!props.environment.authorizeEndpoint && !!props.environment.clientId && !!props.environment.redirectUri;

  useEffect(() => { onAuthenticatedRef.current = props.onAuthenticated; }, [props.onAuthenticated]);
  useEffect(() => { onErrorRef.current = props.onError; }, [props.onError]);
  useEffect(() => { onSignedOutRef.current = props.onSignedOut; }, [props.onSignedOut]);

  useEffect(() => {
    if (environmentIdRef.current === props.environment.id) return;
    environmentIdRef.current = props.environment.id;
    generationRef.current++;
    controllerRef.current?.abort();
    controllerRef.current = null;
    pendingRef.current = null;
    popupRef.current?.close();
    popupRef.current = null;
    setPendingCallback(false);
    setCallbackUrl('');
    setRevealCallbackUrl(false);
    setSession(null);
    setBusy(false);
    setError('');
  }, [props.environment.id]);

  useEffect(() => () => {
    generationRef.current++;
    controllerRef.current?.abort();
    popupRef.current?.close();
  }, []);

  const reportError = (cause: unknown) => {
    const text = cause instanceof CumulusAuthenticationError
      ? cause.localizedMessage(language)
      : message(cause, language);
    setError(text);
    onErrorRef.current(text);
  };

  const connectMock = () => {
    const accessToken = props.environment.mockAccessToken;
    if (!canUseMock || !accessToken) return;
    const token: CumulusSessionToken = {
      kind: 'mock',
      accessToken,
      refreshToken: null,
      accessExpiresUtc: null,
      refreshExpiresUtc: null,
    };
    setSession(token);
    setError('');
    onAuthenticatedRef.current(token);
  };

  const startSignIn = () => {
    if (!canStartSignIn || interactionBusy) return;
    setError('');
    const openExternal = window.hecDesktop?.openExternalUrl;
    const popup = openExternal ? null : window.open('about:blank', '_blank', 'popup,width=900,height=750,resizable=yes,scrollbars=yes');
    if (!openExternal && !popup) {
      reportError(new Error(label('浏览器阻止了登录弹窗。请允许此站点打开弹窗后重试。', 'The browser blocked the sign-in popup. Allow popups for this site and retry.')));
      return;
    }
    // The callback is supplied by the identity provider; it must never be able to navigate this page.
    try { if (popup) popup.opener = null; } catch { /* Some embedded browsers expose a readonly opener. */ }
    popupRef.current = popup;
    const generation = ++generationRef.current;
    const controller = new AbortController();
    controllerRef.current?.abort();
    controllerRef.current = controller;
    setBusy(true);

    void (async () => {
      try {
        const attempt = await createCumulusAuthorizationAttempt(props.environment);
        if (controller.signal.aborted || generation !== generationRef.current || environmentIdRef.current !== attempt.environmentId) return;
        pendingRef.current = { state: attempt.state, verifier: attempt.verifier, environmentId: attempt.environmentId };
        setPendingCallback(true);
        if (openExternal) await openExternal(attempt.authorizeUrl);
        else popup?.location.replace(attempt.authorizeUrl);
      } catch (cause) {
        if (!controller.signal.aborted && generation === generationRef.current) {
          popup?.close();
          popupRef.current = null;
          pendingRef.current = null;
          setPendingCallback(false);
          reportError(cause);
        }
      } finally {
        if (generation === generationRef.current) setBusy(false);
      }
    })();
  };

  const discardPendingLogin = () => {
    generationRef.current++;
    controllerRef.current?.abort();
    controllerRef.current = null;
    pendingRef.current = null;
    popupRef.current?.close();
    popupRef.current = null;
    setPendingCallback(false);
    setCallbackUrl('');
    setRevealCallbackUrl(false);
    setBusy(false);
  };

  const completeSignIn = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const pending = pendingRef.current;
    if (!pending || !props.environment.redirectUri || interactionBusy) return;
    let code: string;
    try {
      code = parseCumulusCallbackUrl(props.environment.redirectUri, callbackUrl, pending.state).code;
    } catch (cause) {
      reportError(cause);
      if (cause instanceof CumulusAuthenticationError &&
          (cause.reason === 'stateMismatch' || cause.reason === 'authorizationRejected' || cause.reason === 'missingCode')) {
        discardPendingLogin();
      }
      return;
    }

    const verifier = pending.verifier;
    const environmentId = pending.environmentId;
    const generation = ++generationRef.current;
    const controller = new AbortController();
    controllerRef.current?.abort();
    controllerRef.current = controller;
    setBusy(true);
    setError('');
    setCallbackUrl('');
    try {
      const tokenSet = await exchangeCumulusAuthorizationCode(props.backend, environmentId, code, verifier, controller.signal);
      if (controller.signal.aborted || generation !== generationRef.current || environmentIdRef.current !== environmentId) return;
      acceptTokenSet(tokenSet, 'authenticated');
      pendingRef.current = null;
      setPendingCallback(false);
      popupRef.current?.close();
      popupRef.current = null;
    } catch (cause) {
      if (!controller.signal.aborted && generation === generationRef.current) {
        pendingRef.current = null;
        setPendingCallback(false);
        popupRef.current?.close();
        popupRef.current = null;
        reportError(cause);
      }
    } finally {
      if (generation === generationRef.current) setBusy(false);
    }
  };

  const acceptTokenSet = (tokenSet: CumulusTokenSet, kind: 'authenticated') => {
    const token: CumulusSessionToken = { ...tokenSet, kind };
    setSession(token);
    onAuthenticatedRef.current(token);
  };

  const refresh = async () => {
    if (!session || session.kind !== 'authenticated' || !session.refreshToken || interactionBusy) return;
    const generation = ++generationRef.current;
    const controller = new AbortController();
    controllerRef.current?.abort();
    controllerRef.current = controller;
    setBusy(true);
    setError('');
    try {
      const tokenSet = await refreshCumulusToken(props.backend, props.environment.id, session.refreshToken, controller.signal);
      if (controller.signal.aborted || generation !== generationRef.current || environmentIdRef.current !== props.environment.id) return;
      // Some identity providers rotate refresh tokens; when they do not, retain the still-valid prior token in memory.
      acceptTokenSet({ ...tokenSet, refreshToken: tokenSet.refreshToken ?? session.refreshToken }, 'authenticated');
    } catch (cause) {
      if (!controller.signal.aborted && generation === generationRef.current) reportError(cause);
    } finally {
      if (generation === generationRef.current) setBusy(false);
    }
  };

  const signOut = () => {
    discardPendingLogin();
    setSession(null);
    setError('');
    onSignedOutRef.current?.();
  };

  const expires = session?.accessExpiresUtc ? formatExpiry(session.accessExpiresUtc, language) : null;

  return <section className="cumulus-login" aria-label={label('Cumulus 账户登录', 'Cumulus account sign-in')} data-testid="cumulus-login-window">
    <header className="cumulus-login__header">
      <div className="cumulus-login__heading">
        <span className="cumulus-login__eyebrow">{label('格网数据源', 'Gridded data source')}</span>
        <h3>{label('Cumulus 登录', 'Cumulus sign-in')}</h3>
        <p>{label('登录状态只保存在当前应用内存中；关闭页面或应用后需要重新登录。', 'Sign-in state is kept only in this application’s memory. Closing the page or app requires signing in again.')}</p>
      </div>
      <span className={`cumulus-login__environment${props.environment.mockAccessToken ? ' is-mock' : ''}`}>
        {props.environment.name}
        {props.environment.mockAccessToken && <span>{label('模拟环境', 'Mock')}</span>}
      </span>
    </header>

    {session ? <div className={`cumulus-login__session${session.kind === 'mock' ? ' is-mock' : ''}`} role="status" data-testid="cumulus-session-status">
      <div className="cumulus-login__session-copy">
        <strong>{session.kind === 'mock'
          ? label('已连接本机模拟环境', 'Connected to the local mock environment')
          : label('已连接 Cumulus', 'Connected to Cumulus')}</strong>
        <span>{session.kind === 'mock'
          ? label('此模拟令牌仅用于本地开发验证，不代表真实账户登录。', 'This mock token is for local development only; it is not a real account sign-in.')
          : expires ? label(`访问令牌有效期至 ${expires}`, `Access token expires ${expires}`)
            : label('服务端未提供访问令牌到期时间。', 'The server did not provide an access-token expiry.')}</span>
      </div>
      <div className="cumulus-login__session-actions">
        {session.kind === 'authenticated' && session.refreshToken && <button type="button" className="cumulus-login__button" disabled={interactionBusy} onClick={() => { void refresh(); }}>
          {busy ? label('正在刷新…', 'Refreshing…') : label('刷新令牌', 'Refresh token')}
        </button>}
        <button type="button" className="cumulus-login__button cumulus-login__button--quiet" disabled={interactionBusy} onClick={signOut}>{label('断开', 'Disconnect')}</button>
      </div>
    </div> : <div className="cumulus-login__actions">
      {canUseMock && <button type="button" className="cumulus-login__button cumulus-login__button--primary" disabled={interactionBusy} onClick={connectMock}>
        {label('连接本机模拟环境', 'Connect local mock environment')}
      </button>}
      {canStartSignIn && <button type="button" className="cumulus-login__button cumulus-login__button--primary" disabled={interactionBusy} onClick={startSignIn}>
        {busy ? label('正在打开登录…', 'Opening sign-in…') : label('在新窗口登录', 'Sign in in a new window')}
      </button>}
      {!canUseMock && !canStartSignIn && <p className="cumulus-login__muted">{props.environment.isSelectable
        ? label('此环境尚未配置登录或模拟令牌。', 'This environment has no sign-in or mock-token configuration.')
        : label('此环境暂不可选择。', 'This environment is not currently selectable.')}</p>}
    </div>}

    {canStartSignIn && <p className="cumulus-login__callback-note">
      {label('当前注册回调地址', 'Registered callback URI')}: <code>{props.environment.redirectUri}</code>。{label('完成身份验证后，如登录窗口无法打开该地址，请复制地址栏中的完整回调 URL 并粘贴到下方。应用不会读取或记录弹窗中的授权码。', 'After authentication, if the login window cannot open this address, copy the full callback URL from its address bar and paste it below. The app does not read or log the authorization code from the popup.')}
    </p>}

    {pendingCallback && <form className="cumulus-login__callback" onSubmit={(event) => { void completeSignIn(event); }}>
      <label htmlFor="cumulus-callback-url">{label('粘贴完整回调 URL', 'Paste the full callback URL')}</label>
      <div className="cumulus-login__callback-row">
        <input
          id="cumulus-callback-url"
          data-testid="cumulus-callback-url"
          type={revealCallbackUrl ? 'text' : 'password'}
          autoComplete="off"
          autoCapitalize="off"
          spellCheck={false}
          value={callbackUrl}
          onChange={(event) => { setCallbackUrl(event.currentTarget.value); setError(''); }}
          placeholder={props.environment.redirectUri ?? ''}
          disabled={interactionBusy}
          aria-describedby="cumulus-callback-help"
        />
        <button type="button" className="cumulus-login__button cumulus-login__button--quiet" disabled={interactionBusy} onClick={() => setRevealCallbackUrl((value) => !value)}>
          {revealCallbackUrl ? label('隐藏', 'Hide') : label('显示', 'Show')}
        </button>
      </div>
      <span id="cumulus-callback-help" className="cumulus-login__muted">{label('完整 URL 只用于本次 state 校验和授权码交换，不会写入浏览器存储。', 'The full URL is used only for this attempt’s state validation and code exchange. It is never written to browser storage.')}</span>
      <div className="cumulus-login__actions">
        <button type="submit" className="cumulus-login__button cumulus-login__button--primary" disabled={!callbackUrl.trim() || interactionBusy}>
          {busy ? label('正在验证…', 'Verifying…') : label('验证并连接', 'Verify and connect')}
        </button>
        <button type="button" className="cumulus-login__button cumulus-login__button--quiet" disabled={interactionBusy} onClick={discardPendingLogin}>{label('取消登录', 'Cancel sign-in')}</button>
      </div>
    </form>}

    {error && <p className="cumulus-login__error" role="alert" data-testid="cumulus-login-error">{error}</p>}
  </section>;
}

function formatExpiry(value: string, language: Language): string {
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) return value;
  try {
    return new Intl.DateTimeFormat(language === 'en' ? 'en-US' : 'zh-CN', {
      dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC',
    }).format(timestamp) + ' UTC';
  } catch {
    return new Date(timestamp).toISOString();
  }
}
