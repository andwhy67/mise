import { X } from 'lucide-react'
import { useEffect, useId, useRef, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import styles from './Sheet.module.scss'

interface SheetProps {
  open: boolean
  onClose: () => void
  title: string
  /** Пояснение под заголовком. */
  subtitle?: ReactNode
  children: ReactNode
  /** Кнопки внизу листа (на телефоне прилипают к нижнему краю). */
  footer?: ReactNode
  /** Широкий лист — для форм со строками (поставка, состав блюда). */
  wide?: boolean
}

/**
 * Лист для форм (DESIGN.md §3): на телефоне — во весь экран снизу, на широком — панель по центру.
 * Нативный <dialog>: фокус внутри, Esc и клик по подложке закрывают, фокус возвращается к кнопке-источнику.
 */
export function Sheet({ open, onClose, title, subtitle, children, footer, wide = false }: SheetProps) {
  const { t } = useTranslation()
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog
      ref={ref}
      className={`${styles.sheet} ${wide ? styles.wide : ''}`}
      aria-labelledby={titleId}
      onClose={onClose}
      onClick={(event) => {
        // Клик по подложке: целью события становится сам <dialog>, а не его содержимое.
        if (event.target === event.currentTarget) onClose()
      }}
    >
      {open && (
        <div className={styles.inner}>
          <header className={styles.header}>
            <div>
              <h2 id={titleId} className={styles.title}>
                {title}
              </h2>
              {subtitle && <p className={styles.subtitle}>{subtitle}</p>}
            </div>
            <button type="button" className={styles.close} aria-label={t('actions.close')} onClick={onClose}>
              <X aria-hidden="true" strokeWidth={1.8} />
            </button>
          </header>
          <div className={styles.body}>{children}</div>
          {footer && <footer className={styles.footer}>{footer}</footer>}
        </div>
      )}
    </dialog>
  )
}
