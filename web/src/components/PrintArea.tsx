import { useEffect, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import styles from './PrintArea.module.scss'

/**
 * Печать отдельного документа (заявка поставщику): содержимое рисуется в узел прямо в <body>,
 * на время печати всё остальное скрыто (класс `printing` на body, styles/global.scss). «Сохранить как PDF» —
 * штатный пункт диалога печати браузера, отдельная библиотека не нужна.
 */
export function usePrint() {
  const [content, setContent] = useState<ReactNode | null>(null)

  useEffect(() => {
    if (content === null) return
    document.body.classList.add('printing')
    const done = () => {
      document.body.classList.remove('printing')
      setContent(null)
    }
    window.addEventListener('afterprint', done, { once: true })
    // Дать React дорисовать документ; rAF в фоне/на паузе не срабатывает — таймер надёжнее.
    const timer = window.setTimeout(() => window.print(), 50)
    return () => {
      window.clearTimeout(timer)
      window.removeEventListener('afterprint', done)
      document.body.classList.remove('printing')
    }
  }, [content])

  const portal = content === null ? null : createPortal(<div className={`print-root ${styles.root}`}>{content}</div>, document.body)
  return { print: (node: ReactNode) => setContent(node), portal }
}
