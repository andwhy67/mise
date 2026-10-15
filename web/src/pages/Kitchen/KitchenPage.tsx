import { useQuery } from '@tanstack/react-query'
import { Maximize2, Minimize2, Volume2, VolumeX } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '@/api/client'
import { queryKeys } from '@/api/keys'
import { useOrderStatus } from '@/api/mutations'
import { Button } from '@/components/Button'
import { Page } from '@/components/Page'
import { ErrorState, LoadingState } from '@/components/StateBlocks'
import { chime, unlockSound } from '@/lib/chime'
import { toast } from '@/lib/toast'
import { useErrorText } from '@/lib/useErrorText'
import { useFormat, useNow } from '@/lib/useFormat'
import { vibrate } from '@/pwa/haptics'
import { COLUMNS, NEXT_STATUS, byColumn, elapsedMs, formatTimer, lateness, newlyCreated, type KitchenOrder, type KitchenStatus } from './kitchen'
import styles from './KitchenPage.module.scss'

const SOUND_KEY = 'mise.kitchenSound'
/** Страховка к live-обновлениям: если событие потерялось, экран догонит сам. */
const REFRESH_MS = 30_000

function readSound() {
  try {
    return localStorage.getItem(SOUND_KEY) !== '0'
  } catch {
    return true
  }
}

/** Экран не гаснет, пока открыт экран кухни (планшет на раздаче); при возврате во вкладку — снова. */
function useScreenAwake() {
  useEffect(() => {
    if (!('wakeLock' in navigator)) return
    let sentinel: WakeLockSentinel | null = null
    let alive = true
    const acquire = async () => {
      if (!alive || document.visibilityState !== 'visible' || sentinel) return
      try {
        sentinel = await navigator.wakeLock.request('screen')
        sentinel.addEventListener('release', () => (sentinel = null))
      } catch {
        // Отказ браузера (экономия батареи) — экран погаснет как обычно.
      }
    }
    const onVisible = () => void acquire()
    document.addEventListener('visibilitychange', onVisible)
    void acquire()
    return () => {
      alive = false
      document.removeEventListener('visibilitychange', onVisible)
      void sentinel?.release()
    }
  }, [])
}

/**
 * Экран кухни (KDS): три колонки — Принят, Готовится, Готов. Таймер — сколько заказ в колонке, цвет — пора ли
 * торопиться. Новый заказ — звонок и вспышка карточки. Во весь экран — для планшета на раздаче.
 */
export function KitchenPage() {
  const { t } = useTranslation('kitchen')
  const board = useRef<HTMLDivElement>(null)
  const [fullscreen, setFullscreen] = useState(false)
  const [sound, setSound] = useState(readSound)
  const [fresh, setFresh] = useState<ReadonlySet<string>>(new Set())
  const known = useRef<Set<string> | null>(null)
  const query = useQuery({
    queryKey: [...queryKeys.orders, 'kitchen'],
    queryFn: ({ signal }) => api.get<KitchenOrder[]>('/api/v1/kitchen/orders', { signal }),
    refetchInterval: REFRESH_MS,
  })
  useScreenAwake()

  // Звук разрешается первым касанием экрана (правило браузеров) — дальше звонок работает без кнопок.
  useEffect(() => {
    const unlock = () => unlockSound()
    document.addEventListener('pointerdown', unlock, { once: true })
    return () => document.removeEventListener('pointerdown', unlock)
  }, [])

  useEffect(() => {
    const onChange = () => setFullscreen(document.fullscreenElement === board.current)
    document.addEventListener('fullscreenchange', onChange)
    return () => document.removeEventListener('fullscreenchange', onChange)
  }, [])

  // Новые принятые заказы: звонок, вибрация, вспышка карточки на несколько секунд.
  useEffect(() => {
    if (!query.data) return
    const added = newlyCreated(known.current, query.data)
    known.current = new Set(query.data.map((o) => o.id))
    if (added.length === 0) return
    if (sound) chime()
    vibrate('success')
    setFresh(new Set(added))
    const timer = window.setTimeout(() => setFresh(new Set()), 6000)
    return () => window.clearTimeout(timer)
  }, [query.data, sound])

  const toggleSound = () => {
    const next = !sound
    setSound(next)
    try {
      localStorage.setItem(SOUND_KEY, next ? '1' : '0')
    } catch {
      // Приватный режим — выбор не запомнится.
    }
    if (next) {
      unlockSound()
      chime()
    }
  }

  const toggleFullscreen = () => {
    if (document.fullscreenElement) void document.exitFullscreen()
    else void board.current?.requestFullscreen?.().catch(() => toast.show(t('fullscreenFailed'), 'warn'))
  }

  const columns = byColumn(query.data ?? [])
  const total = query.data?.length ?? 0

  return (
    <Page
      title={t('title')}
      subtitle={t('subtitle')}
      actions={
        <span className={styles.actions}>
          <Button variant="secondary" icon={sound ? <Volume2 /> : <VolumeX />} aria-pressed={sound} onClick={toggleSound}>
            {t(sound ? 'soundOn' : 'soundOff')}
          </Button>
          <Button variant="secondary" icon={<Maximize2 />} onClick={toggleFullscreen}>
            {t('fullscreen')}
          </Button>
        </span>
      }
    >
      <div ref={board} className={`${styles.board} ${fullscreen ? styles.fullscreen : ''}`}>
        {fullscreen && (
          <div className={styles.boardHead}>
            <span className={styles.boardTitle}>{t('title')}</span>
            <Clock />
            <span className={styles.boardCount}>{t('inWork', { count: total })}</span>
            <Button variant="secondary" icon={sound ? <Volume2 /> : <VolumeX />} aria-pressed={sound} onClick={toggleSound}>
              <span className="visually-hidden">{t(sound ? 'soundOn' : 'soundOff')}</span>
            </Button>
            <Button variant="secondary" icon={<Minimize2 />} onClick={toggleFullscreen}>
              {t('exitFullscreen')}
            </Button>
          </div>
        )}
        {query.isPending ? (
          <LoadingState lines={6} />
        ) : query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <div className={styles.columns}>
            {COLUMNS.map((status) => (
              <Column key={status} status={status} orders={columns[status]} fresh={fresh} />
            ))}
          </div>
        )}
      </div>
    </Page>
  )
}

function Clock() {
  const f = useFormat()
  const now = useNow(1000)
  return <span className={styles.clock}>{f.time(now)}</span>
}

function Column({ status, orders, fresh }: { status: KitchenStatus; orders: KitchenOrder[]; fresh: ReadonlySet<string> }) {
  const { t } = useTranslation('kitchen')
  const id = `kitchen-${status}`
  return (
    <section className={styles.column} data-status={status} aria-labelledby={id}>
      <h2 id={id} className={styles.columnTitle}>
        {t(`columns.${status}`)}
        <span className={styles.columnCount}>{orders.length}</span>
      </h2>
      {orders.length === 0 ? (
        <p className={styles.empty}>{t(`empty.${status}`)}</p>
      ) : (
        <ol className={styles.cards}>
          {orders.map((order) => (
            <OrderCard key={order.id} order={order} fresh={fresh.has(order.id)} />
          ))}
        </ol>
      )}
    </section>
  )
}

function OrderCard({ order, fresh }: { order: KitchenOrder; fresh: boolean }) {
  const { t } = useTranslation('kitchen')
  const { t: to } = useTranslation('orders')
  const errorText = useErrorText()
  const mutation = useOrderStatus()
  const now = useNow(1000)
  const tone = lateness(order, now)
  const next = NEXT_STATUS[order.status]

  return (
    <li className={`${styles.card} ${fresh ? styles.fresh : ''}`} data-tone={tone}>
      <div className={styles.cardHead}>
        <span className={styles.number}>{order.number}</span>
        <span className={styles.timer} data-tone={tone} aria-label={t('timerLabel', { time: formatTimer(elapsedMs(order.statusSince, now)) })}>
          <span className={styles.square} aria-hidden="true" />
          {formatTimer(elapsedMs(order.statusSince, now))}
        </span>
      </div>
      {order.source === 'Guest' && (
        <p className={styles.guest}>
          {[order.tableNumber ? t('guest.table', { table: order.tableNumber }) : t('guest.noTable'), order.payment ? t(`guest.payment.${order.payment}`) : null]
            .filter(Boolean)
            .join(' · ')}
        </p>
      )}
      <ul className={styles.items}>
        {order.items.map((item, index) => (
          <li key={index}>
            <span className={styles.qty}>{item.quantity}×</span> {item.dishName}
            {item.modifications.length > 0 && (
              <span className={styles.changes}>
                {item.modifications
                  .map((m) => (m.kind === 'Remove' ? to('change.remove', { product: m.productName }) : to('change.replace', { product: m.productName, substitute: m.substituteName })))
                  .join(' · ')}
              </span>
            )}
          </li>
        ))}
      </ul>
      {order.substitutions.length > 0 && (
        <p className={styles.substitutions}>
          {order.substitutions.map((s) => t('substitution', { product: s.productName, substitute: s.substituteName })).join(' · ')}
        </p>
      )}
      <Button
        size="lg"
        block
        variant={order.status === 'Done' ? 'primary' : 'secondary'}
        disabled={mutation.isPending}
        onClick={() => mutation.mutate({ id: order.id, status: next }, { onError: (cause) => toast.show(errorText(cause), 'crit') })}
        aria-label={t(`next.label.${next}`, { number: order.number })}
      >
        {t(`next.${next}`)}
      </Button>
    </li>
  )
}
