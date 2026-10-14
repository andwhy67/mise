import { useTranslation } from 'react-i18next'
import { useLiveStatus, type LiveStatus } from '@/realtime/connection'
import styles from './LiveIndicator.module.scss'

const TONE: Record<LiveStatus, string> = { live: 'live', connecting: 'pending', reconnecting: 'pending', offline: 'offline' }

/** Состояние live-обновлений (DESIGN.md §5): точка + текст; озвучивается при изменении. */
export function LiveIndicator({ short = false, status: forced }: { short?: boolean; status?: LiveStatus }) {
  const { t } = useTranslation()
  const live = useLiveStatus()
  const status = forced ?? live
  const text = status === 'live' ? (short ? t('live.liveShort') : t('live.live')) : t(`live.${status}`)
  return (
    <span className={`${styles.indicator} ${styles[TONE[status]!]}`} role="status" aria-live="polite">
      <span className={styles.dot} aria-hidden="true" />
      {text}
    </span>
  )
}
