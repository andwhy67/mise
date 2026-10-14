import { useQueryClient } from '@tanstack/react-query'
import { RefreshCw } from 'lucide-react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { isStandalone } from './install'
import styles from './PullToRefresh.module.scss'

/** Сколько пикселей нужно протянуть, чтобы обновить (с «сопротивлением»: палец проходит больше). */
const THRESHOLD = 72
const MAX_PULL = 110

/**
 * «Потянуть вниз — обновить» в установленном приложении на телефоне: у PWA нет кнопки «Обновить» браузера.
 * В обычной вкладке браузер делает это сам — там компонент ничего не меняет.
 * Обновляются данные (активные запросы), а не вся страница: корзина и введённое в формы не пропадают.
 */
export function PullToRefresh({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  const client = useQueryClient()
  const [pull, setPull] = useState(0)
  const [busy, setBusy] = useState(false)
  const start = useRef<number | null>(null)
  const pullRef = useRef(0)

  useEffect(() => {
    if (!isStandalone() || !window.matchMedia('(pointer: coarse)').matches) return

    const onStart = (event: TouchEvent) => {
      // Только от самого верха страницы и не внутри открытого листа (<dialog>).
      start.current = window.scrollY <= 0 && !document.querySelector('dialog[open]') ? event.touches[0]!.clientY : null
    }
    const onMove = (event: TouchEvent) => {
      if (start.current === null) return
      const distance = event.touches[0]!.clientY - start.current
      const next = distance > 0 ? Math.min(MAX_PULL, distance * 0.5) : 0
      pullRef.current = next
      setPull(next)
    }
    const onEnd = () => {
      if (start.current === null) return
      start.current = null
      if (pullRef.current >= THRESHOLD * 0.75) {
        setBusy(true)
        void client.refetchQueries({ type: 'active' }).finally(() => setBusy(false))
      }
      pullRef.current = 0
      setPull(0)
    }

    window.addEventListener('touchstart', onStart, { passive: true })
    window.addEventListener('touchmove', onMove, { passive: true })
    window.addEventListener('touchend', onEnd)
    return () => {
      window.removeEventListener('touchstart', onStart)
      window.removeEventListener('touchmove', onMove)
      window.removeEventListener('touchend', onEnd)
    }
  }, [client])

  const shown = busy || pull > 8
  const ready = pull >= THRESHOLD * 0.75
  return (
    <>
      <div
        className={`${styles.indicator} ${shown ? styles.visible : ''} ${busy ? styles.busy : ''}`}
        style={{ transform: `translate(-50%, ${busy ? 56 : pull - 40}px)` }}
        role="status"
        aria-live="polite"
      >
        <RefreshCw aria-hidden="true" strokeWidth={1.8} style={busy ? undefined : { transform: `rotate(${pull * 3}deg)` }} />
        <span className="visually-hidden">{busy ? t('pwa.refreshing') : ready ? t('pwa.releaseToRefresh') : ''}</span>
      </div>
      {children}
    </>
  )
}
