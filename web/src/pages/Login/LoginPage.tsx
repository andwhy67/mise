import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, useLocation, useNavigate, useParams } from 'react-router'
import { useAuthConfig } from '@/api/queries'
import { canAccess } from '@/app/navigation'
import { session, useSession, type UserRole } from '@/auth/session'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { MiseWordmark } from '@/components/MiseWordmark'
import { Segmented } from '@/components/Segmented'
import { LanguageSwitcher, ThemeSwitcher } from '@/components/Switchers'
import { useErrorText } from '@/lib/useErrorText'
import { JoinForm, RegisterForm } from './AccountForms'
import styles from './LoginPage.module.scss'

/** Вход, новый ресторан или вход по приглашению — у каждого режима свой адрес (ссылку-приглашение просто открывают). */
export type AuthMode = 'login' | 'register' | 'join'
const MODE_PATH: Record<AuthMode, string> = { login: '/login', register: '/register', join: '/join' }

interface LocationState {
  from?: string
  reason?: 'logout' | 'expired' | null
}

/**
 * Вход (DESIGN.md §6.1): типографская синяя половина + форма; демо-доступ — только если включён на сервере.
 * Аккаунт появляется только двумя путями: создать свой ресторан или войти в существующий по приглашению.
 */
export function LoginPage({ mode = 'login' }: { mode?: AuthMode }) {
  const { t } = useTranslation('auth')
  const errorText = useErrorText()
  const navigate = useNavigate()
  const location = useLocation()
  const { status, user } = useSession()
  const demo = useAuthConfig().data
  const state = (location.state ?? {}) as LocationState
  const { code } = useParams()
  // Вернуть на прежнюю страницу — только после истёкшей сессии и только если новой роли туда можно.
  const targetFor = (role: UserRole | undefined) =>
    state.reason !== 'logout' && state.from && state.from !== '/login' && role && canAccess(role, state.from) ? state.from : '/'

  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (status === 'signedIn') return <Navigate to={targetFor(user?.role)} replace />

  const run = async (action: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try {
      await action()
      navigate(targetFor(session.snapshot().user?.role), { replace: true })
    } catch (cause) {
      setError(errorText(cause))
    } finally {
      setBusy(false)
    }
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (!login.trim() || !password) {
      setError(t('required'))
      return
    }
    void run(() => session.login(login.trim(), password))
  }

  const notice = state.reason === 'expired' ? t('expired') : state.reason === 'logout' ? t('loggedOut') : null
  const done = () => navigate('/', { replace: true })

  return (
    <div className={styles.page}>
      <section className={styles.hero} aria-hidden="false">
        <MiseWordmark inverse size="lg" />
        <div className={styles.heroText}>
          <h1 className={styles.heroTitle} lang="fr">
            {t('heroTitle')} <em>{t('heroAccent')}</em>
          </h1>
          <p className={styles.transcription}>{t('transcription')}</p>
          <p className={styles.lead}>{t('heroText')}</p>
        </div>
      </section>

      <section className={styles.formSide}>
        <div className={styles.controls}>
          <ThemeSwitcher />
          <LanguageSwitcher />
        </div>
        <div className={styles.formWrap}>
          <div className={styles.modes}>
            <Segmented
              label={t('account.modes')}
              value={mode}
              options={(['login', 'register', 'join'] as const).map((value) => ({ value, label: t(`account.mode.${value}`) }))}
              onChange={(value) => navigate(MODE_PATH[value], { replace: true, state })}
            />
          </div>
          <h2 className={styles.title}>{t(mode === 'login' ? 'title' : `account.title.${mode}`)}</h2>
          <p className={styles.greeting}>{t(mode === 'login' ? 'greeting' : `account.lead.${mode}`)}</p>
          {mode === 'login' && notice && <p className={styles.notice}>{notice}</p>}

          {mode === 'register' && <RegisterForm onDone={done} />}
          {mode === 'join' && <JoinForm initialCode={code} onDone={done} />}
          {mode === 'login' && (
          <form className={styles.form} onSubmit={submit} noValidate>
            <Field label={t('login')} name="username" autoComplete="username" value={login} onChange={(e) => setLogin(e.target.value)} />
            <Field
              label={t('password')}
              name="password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            <p className={styles.error} aria-live="polite">
              {error}
            </p>
            <Button type="submit" size="lg" block disabled={busy}>
              {busy ? t('submitting') : t('submit')}
            </Button>
          </form>
          )}

          {mode === 'login' && demo?.demoEnabled && demo.demoRoles.length > 0 && (
            <p className={styles.demo}>
              <span>{t('demo')}</span>
              {(['Warehouse', 'Chef', 'Manager'] as const)
                .filter((role) => demo.demoRoles.includes(role))
                .map((role: Exclude<UserRole, 'Simulator'>, index) => (
                  <span key={role} className={styles.demoItem}>
                    {index > 0 && <span aria-hidden="true">·</span>}
                    <Button variant="text" disabled={busy} onClick={() => void run(() => session.demoLogin(role))}>
                      {t(`demoRoles.${role}`)}
                    </Button>
                  </span>
                ))}
            </p>
          )}

          <p className={styles.slogan}>{t('slogan')}</p>
        </div>
      </section>
    </div>
  )
}
