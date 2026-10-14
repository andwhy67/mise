import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { useAuthConfig } from '@/api/queries'
import { canAccess } from '@/app/navigation'
import { session, useSession, type UserRole } from '@/auth/session'
import { Button } from '@/components/Button'
import { Field } from '@/components/Field'
import { MiseWordmark } from '@/components/MiseWordmark'
import { LanguageSwitcher, ThemeSwitcher } from '@/components/Switchers'
import { useErrorText } from '@/lib/useErrorText'
import styles from './LoginPage.module.scss'

interface LocationState {
  from?: string
  reason?: 'logout' | 'expired' | null
}

/** Вход (DESIGN.md §6.1): типографская синяя половина + форма; демо-доступ — только если включён на сервере. */
export function LoginPage() {
  const { t } = useTranslation('auth')
  const errorText = useErrorText()
  const navigate = useNavigate()
  const location = useLocation()
  const { status, user } = useSession()
  const demo = useAuthConfig().data
  const state = (location.state ?? {}) as LocationState
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
          <h2 className={styles.title}>{t('title')}</h2>
          <p className={styles.greeting}>{t('greeting')}</p>
          {notice && <p className={styles.notice}>{notice}</p>}

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

          {demo?.demoEnabled && demo.demoRoles.length > 0 && (
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
