import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink, Navigate, Outlet, useLocation } from 'react-router'
import { useAlertSummary, useStopList } from '@/api/queries'
import { NAV, canAccess, mobileTabsFor, navFor } from '@/app/navigation'
import { session, userLine, useSession, type SessionUser } from '@/auth/session'
import { LiveIndicator } from '@/components/LiveIndicator'
import { MiseWordmark } from '@/components/MiseWordmark'
import { LanguageSwitcher, ThemeSwitcher } from '@/components/Switchers'
import { Toaster } from '@/components/Toaster'
import { NoAccessPage } from '@/pages/Misc/MiscPages'
import { realtime } from '@/realtime/connection'
import styles from './AppLayout.module.scss'

/** Путь ведёт в один из разделов навигации (а не в 404). */
const isSection = (pathname: string) => NAV.some((entry) => entry.path !== '/' && pathname.startsWith(entry.path))

/** Защищённая зона: восстанавливает сессию по cookie, держит live-соединение, рисует навигацию по роли. */
export function AppLayout() {
  const state = useSession()
  const location = useLocation()
  const queryClient = useQueryClient()

  useEffect(() => {
    void session.restore()
  }, [])

  useEffect(() => {
    if (state.status !== 'signedIn') return
    realtime.start(queryClient)
    return () => void realtime.stop()
  }, [state.status, queryClient])

  if (state.status === 'unknown') return <div className={styles.splash} aria-busy="true" />
  if (state.status === 'signedOut' || !state.user)
    return <Navigate to="/login" replace state={{ from: location.pathname, reason: state.reason }} />

  return <Shell user={state.user} />
}

function Shell({ user }: { user: SessionUser }) {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const items = navFor(user.role)
  const stopCount = useStopList().data?.length ?? 0
  const alerts = useAlertSummary().data
  // Счётчики — числом текстом, без плашек (DESIGN.md §5, NavItem): стоп-лист красным, уведомления — красным только при критичных.
  const counter = (id: string) => {
    if (id === 'stoplist' && stopCount > 0)
      return (
        <span className={styles.count} aria-label={`${t('nav.stopList')}: ${stopCount}`}>
          {stopCount}
        </span>
      )
    if (id === 'alerts' && alerts && alerts.total > 0)
      return (
        <span className={`${styles.count} ${alerts.critical ? '' : styles.countQuiet}`} aria-label={`${t('nav.alerts')}: ${alerts.total}`}>
          {alerts.total}
        </span>
      )
    return null
  }

  return (
    <div className={styles.shell}>
      <a href="#content" className={styles.skip}>
        {t('nav.main')}
      </a>

      {/* Десктоп ≥ 1024: сайдбар без иконок (DESIGN.md §5, NavItem). */}
      <aside className={styles.sidebar}>
        <div className={styles.brand}>
          <MiseWordmark />
        </div>
        <nav aria-label={t('nav.main')} className={styles.nav}>
          {[false, true].map((secondary) => (
            <ul key={String(secondary)} className={styles.navGroup}>
              {items
                .filter((item) => Boolean(item.secondary) === secondary)
                .map((item) => (
                  <li key={item.id}>
                    <NavLink to={item.path} end={item.path === '/'} className={({ isActive }) => `${styles.navItem} ${isActive ? styles.active : ''}`}>
                      <span>{t(item.label)}</span>
                      {counter(item.id)}
                    </NavLink>
                  </li>
                ))}
            </ul>
          ))}
        </nav>
        <footer className={styles.sidebarFooter}>
          <p className={styles.user}>{userLine(user, t(`roles.${user.role}`))}</p>
          <div className={styles.footerControls}>
            <ThemeSwitcher />
            <LanguageSwitcher />
          </div>
          <button type="button" className={styles.logout} onClick={() => void session.logout()}>
            {t('actions.logout')}
          </button>
        </footer>
      </aside>

      {/* Планшет 768–1023: верхняя панель с горизонтальной навигацией. Телефон: шапка + нижняя навигация. */}
      <header className={styles.topbar}>
        <MiseWordmark />
        <nav aria-label={t('nav.main')} className={styles.topNav}>
          {items.map((item) => (
            <NavLink key={item.id} to={item.path} end={item.path === '/'} className={({ isActive }) => `${styles.topItem} ${isActive ? styles.active : ''}`}>
              {t(item.label)}
              {counter(item.id)}
            </NavLink>
          ))}
        </nav>
        <span className={styles.mobileLive}>
          <LiveIndicator short />
        </span>
      </header>

      <main id="content" className={styles.main} tabIndex={-1}>
        {/* key по пути: при переходе экран мягко появляется заново. */}
        <div key={pathname} className={styles.page}>
          {/* Раздел чужой роли — понятный экран вместо ошибки 403 от API. Неизвестный путь — 404 из роутера. */}
          {isSection(pathname) && !canAccess(user.role, pathname) ? <NoAccessPage /> : <Outlet />}
        </div>
      </main>

      <nav aria-label={t('nav.main')} className={styles.bottomNav}>
        {mobileTabsFor(user.role).map((item) => (
          <NavLink key={item.id} to={item.path} end={item.path === '/'} className={({ isActive }) => `${styles.tab} ${isActive ? styles.tabActive : ''}`}>
            <item.icon aria-hidden="true" strokeWidth={1.8} />
            <span>{t(item.shortLabel ?? item.label)}</span>
          </NavLink>
        ))}
      </nav>

      <Toaster />
    </div>
  )
}
