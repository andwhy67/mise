import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router'
import { moreFor } from '@/app/navigation'
import { session, userLine, useSession } from '@/auth/session'
import { LinkButton } from '@/components/Button'
import { Panel } from '@/components/Panel'
import { EmptyState } from '@/components/StateBlocks'
import { LanguageSwitcher, ThemeSwitcher } from '@/components/Switchers'
import { AppSettings } from '@/pwa/AppSettings'
import styles from './MiscPages.module.scss'

/** Раздел не для этой роли (например, повар открыл ссылку на «Пользователей»). */
export function NoAccessPage() {
  const { t } = useTranslation()
  return (
    <div className={styles.page}>
      <Panel>
        <EmptyState
          title={t('state.noAccessTitle')}
          action={
            <LinkButton to="/" variant="text">
              {t('state.toHome')}
            </LinkButton>
          }
        >
          {t('state.noAccessText')}
        </EmptyState>
      </Panel>
    </div>
  )
}

/** «Ещё» на телефоне: остальные разделы роли, оформление, язык, выход. */
export function MorePage() {
  const { t } = useTranslation()
  const user = useSession().user
  if (!user) return null
  return (
    <div className={styles.page}>
      <h1 className={styles.title}>{t('nav.more')}</h1>
      <Panel>
        <ul className={styles.list}>
          {moreFor(user.role).map((item) => (
            <li key={item.id}>
              <NavLink to={item.path} className={styles.link}>
                <item.icon aria-hidden="true" strokeWidth={1.8} />
                {t(item.label)}
              </NavLink>
            </li>
          ))}
        </ul>
      </Panel>
      <Panel title={t('theme.label')}>
        <div className={styles.settings}>
          <ThemeSwitcher withLabels />
          <LanguageSwitcher />
        </div>
      </Panel>
      <AppSettings />
      <p className={styles.user}>{userLine(user, t(`roles.${user.role}`))}</p>
      <button type="button" className={styles.logout} onClick={() => void session.logout()}>
        {t('actions.logout')}
      </button>
    </div>
  )
}

export function NotFoundPage() {
  const { t } = useTranslation()
  return (
    <div className={styles.page}>
      <Panel>
        <EmptyState
          title={t('state.notFoundTitle')}
          action={
            <LinkButton to="/" variant="text">
              {t('state.toHome')}
            </LinkButton>
          }
        >
          {t('state.notFoundText')}
        </EmptyState>
      </Panel>
    </div>
  )
}
