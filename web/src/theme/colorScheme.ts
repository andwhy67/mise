import { useSyncExternalStore } from 'react'

/**
 * Тема интерфейса: «Светлая», «Графит» или как в системе (DESIGN.md §2.1a). Класс .dark на <html> включает токены
 * «Графита». До загрузки React класс ставит скрипт в index.html (без мигания), здесь — переключение и слежение за системой.
 */
export type ThemeChoice = 'light' | 'dark' | 'system'
export const THEME_STORAGE_KEY = 'mise.theme'

const media = () => window.matchMedia('(prefers-color-scheme: dark)')

function read(): ThemeChoice {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY)
    return stored === 'light' || stored === 'dark' ? stored : 'system'
  } catch {
    return 'system'
  }
}

let choice: ThemeChoice = typeof window === 'undefined' ? 'system' : read()
const listeners = new Set<() => void>()

function apply() {
  const dark = choice === 'dark' || (choice === 'system' && media().matches)
  const root = document.documentElement
  root.classList.toggle('dark', dark)
  root.classList.toggle('light', !dark)
  root.dataset.theme = dark ? 'dark' : 'light'
}

export function initColorScheme() {
  apply()
  media().addEventListener('change', () => choice === 'system' && apply())
}

export function setTheme(next: ThemeChoice) {
  choice = next
  try {
    if (next === 'system') localStorage.removeItem(THEME_STORAGE_KEY)
    else localStorage.setItem(THEME_STORAGE_KEY, next)
  } catch {
    // Приватный режим — тема просто не запомнится.
  }
  apply()
  listeners.forEach((listener) => listener())
}

export function useTheme(): { theme: ThemeChoice; setTheme: (next: ThemeChoice) => void } {
  const theme = useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => choice,
    () => choice,
  )
  return { theme, setTheme }
}
