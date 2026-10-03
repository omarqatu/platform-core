import { useEffect, useRef, useState } from 'react';
import { useIntl } from 'react-intl';
import { useLocation, useNavigate, useSearchParams } from 'react-router';
import { ApiError, getJson, isAbort } from '../../api/client';
import { messages as common } from '../../components/messages';
import { ErrorAlert } from '../../components/ErrorAlert';
import { errorMessage } from '../../i18n/apiErrors';
import { safeNext } from '../../session/next';
import { enterable, useSession, type TenantChoice } from '../../session/SessionProvider';
import { messages } from './messages';

type Load = { status: 'loading' } | { status: 'error'; code: string | null } | { status: 'ready'; tenants: TenantChoice[] };

/**
 * The organizations the user can enter — active memberships in active tenants, from GET /tenants (app.user_id alone)
 * — one card each, with what the API returns: the name. Every refusal of a selection (membership_scope_missing,
 * step_up_required, tenant_not_active, not_a_member, …) is shown as it is, and the screen stays.
 */
export function TenantSelectScreen() {
  const intl = useIntl();
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const { selectTenant, signOut } = useSession();
  const next = safeNext(params.get('next'));
  const autoEnter = (location.state as { autoEnter?: boolean } | null)?.autoEnter === true;
  const [load, setLoad] = useState<Load>({ status: 'loading' });
  const [entering, setEntering] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const autoEntered = useRef(false);

  useEffect(() => {
    const abort = new AbortController();
    getJson<{ tenants: TenantChoice[] }>('/tenants', abort.signal).then(
      (body) => setLoad({ status: 'ready', tenants: enterable(body.tenants) }),
      (e: unknown) => isAbort(e) || setLoad({ status: 'error', code: e instanceof ApiError ? e.code : null }),
    );
    return () => abort.abort();
  }, []);

  const enter = async (tenant: TenantChoice) => {
    setEntering(tenant.tenant_id);
    setError(null);
    try {
      await selectTenant(tenant.tenant_id);
      navigate(next ?? '/', { replace: true });
    } catch (e) {
      if (isAbort(e)) return;
      setError(intl.formatMessage(errorMessage(e instanceof ApiError ? e.code : null)));
      setEntering(null);
    }
  };

  // Straight after signing in, a single organization is entered without a choice to make.
  useEffect(() => {
    if (autoEnter && load.status === 'ready' && load.tenants.length === 1 && !autoEntered.current) {
      autoEntered.current = true;
      void enter(load.tenants[0]!);
    }
  });

  if (load.status === 'loading')
    return (
      <main className="tenants">
        <p role="status">{intl.formatMessage(common.loading)}</p>
      </main>
    );
  if (load.status === 'error')
    return (
      <main className="tenants">
        <ErrorAlert message={intl.formatMessage(errorMessage(load.code))} />
      </main>
    );
  if (load.tenants.length === 0)
    return (
      <main className="tenants">
        <h1>{intl.formatMessage(messages.noMembershipTitle)}</h1>
        <p>{intl.formatMessage(messages.noMembershipBody)}</p>
        <button type="button" onClick={() => signOut().finally(() => navigate('/login', { replace: true }))}>
          {intl.formatMessage(common.signOut)}
        </button>
      </main>
    );

  return (
    <main className="tenants">
      <h1>{intl.formatMessage(messages.title)}</h1>
      <ErrorAlert message={error} />
      <p>{intl.formatMessage(messages.intro)}</p>
      <ul className="cards">
        {load.tenants.map((t) => (
          <li key={t.tenant_id}>
            <button type="button" className="card" onClick={() => enter(t)} disabled={entering !== null} aria-busy={entering === t.tenant_id}>
              <bdi dir="auto">{t.name}</bdi>
              {entering === t.tenant_id && <span className="hint">{intl.formatMessage(messages.entering)}</span>}
            </button>
          </li>
        ))}
      </ul>
    </main>
  );
}
