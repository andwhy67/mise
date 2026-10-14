import { useLayoutEffect, useRef, type RefObject } from 'react'

const EASE = 'cubic-bezier(0.2, 0.7, 0.2, 1)'

/**
 * FLIP-перестановка строк при live-обновлении (DESIGN.md §7): строка, сменившая место, плавно доезжает
 * до новой позиции (300 мс) и коротко подсвечивается фоном --brand-tint (600 мс); новая — проявляется.
 * Строки помечаются атрибутом data-flip-key. Позиции — относительно контейнера, поэтому прокрутка не мешает.
 */
export function useFlip(container: RefObject<HTMLElement | null>, version: unknown) {
  const positions = useRef<Map<string, number> | null>(null)

  useLayoutEffect(() => {
    const root = container.current
    if (!root) return
    const items = [...root.querySelectorAll<HTMLElement>('[data-flip-key]')]
    const previous = positions.current
    const next = new Map<string, number>()
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const tint = getComputedStyle(document.documentElement).getPropertyValue('--brand-tint').trim()

    for (const item of items) {
      const key = item.dataset.flipKey!
      const top = item.offsetTop
      next.set(key, top)
      // Первый показ списка — без анимации: двигать нечего.
      if (!previous || reduce) continue
      const before = previous.get(key)
      if (before === undefined) {
        item.animate([{ opacity: 0, transform: 'translateY(8px)' }, { opacity: 1, transform: 'none' }], { duration: 320, easing: EASE })
      } else if (before !== top) {
        item.animate([{ transform: `translateY(${before - top}px)` }, { transform: 'none' }], { duration: 300, easing: EASE })
        if (tint) item.animate([{ backgroundColor: tint }, { backgroundColor: 'transparent' }], { duration: 600, easing: 'ease-out' })
      }
    }
    positions.current = next
  }, [container, version])
}
