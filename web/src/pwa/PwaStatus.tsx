import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useRegisterSW } from 'virtual:pwa-register/react'
import { toast } from '@/lib/toast'
import { useOnline } from './network'
import styles from './PwaStatus.module.scss'

/** Как часто спрашивать сервер о новой версии, пока приложение открыто весь день (смена). */
const UPDATE_CHECK_MS = 60 * 60 * 1000

/** Регистрация service worker и «Доступно обновление → Обновить»: новая версия не подменяет экран посреди заказа. */
export function PwaUpdater() {
  const { t } = useTranslation()
  const shown = useRef(false)
  const {
    needRefresh: [needRefresh],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(_url, registration) {
      if (registration) setInterval(() => void registration.update(), UPDATE_CHECK_MS)
    },
  })

  useEffect(() => {
    if (!needRefresh || shown.current) return
    shown.current = true
    toast.show(t('pwa.updateReady'), 'info', { label: t('pwa.update'), onClick: () => void updateServiceWorker(true) })
  }, [needRefresh, t, updateServiceWorker])

  return null
}

/** «Нет связи — показаны сохранённые данные» (DESIGN.md §8): панель с границей и crit-маркером, без заливки. */
export function OfflineBanner() {
  const { t } = useTranslation()
  const online = useOnline()
  if (online) return null
  return (
    <div className={styles.banner} role="status">
      <span className={styles.square} aria-hidden="true" />
      <p className={styles.text}>
        <strong>{t('pwa.offlineTitle')}</strong> {t('pwa.offlineText')}
      </p>
    </div>
  )
}
