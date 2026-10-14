import { useSyncExternalStore } from 'react'
import type { Tone } from './format'

/** Короткие сообщения о результате действия (DESIGN.md §5, Toast). Критичные не скрываются сами. */
export interface ToastAction {
  label: string
  onClick: () => void
}

export interface Toast {
  id: number
  tone: Tone
  text: string
  /** Кнопка в сообщении («Обновить»); сообщение с действием не скрывается само. */
  action?: ToastAction
}

const AUTO_HIDE_MS = 6_000
const MAX_VISIBLE = 3

let toasts: Toast[] = []
let nextId = 1
const listeners = new Set<() => void>()
const emit = () => listeners.forEach((listener) => listener())

export const toast = {
  show(text: string, tone: Tone = 'fresh', action?: ToastAction) {
    const item: Toast = { id: nextId++, tone, text, action }
    toasts = [...toasts, item].slice(-MAX_VISIBLE)
    emit()
    if (!action && tone !== 'crit' && tone !== 'expired') setTimeout(() => toast.dismiss(item.id), AUTO_HIDE_MS)
    return item.id
  },
  dismiss(id: number) {
    if (!toasts.some((t) => t.id === id)) return
    toasts = toasts.filter((t) => t.id !== id)
    emit()
  },
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => listeners.delete(listener)
  },
  snapshot: () => toasts,
}

export const useToasts = () => useSyncExternalStore(toast.subscribe, toast.snapshot, toast.snapshot)
