import { useSyncExternalStore } from 'react'

/**
 * «Не гасить экран» — для планшета на раздаче: экран не засыпает, стоп-лист и заказы всегда на виду.
 * Выбор запоминается на устройстве. Браузер снимает блокировку при сворачивании — при возврате берём её снова.
 */
const STORAGE_KEY = 'mise.keepAwake'

let enabled = read()
let sentinel: WakeLockSentinel | null = null
const listeners = new Set<() => void>()
const emit = () => listeners.forEach((listener) => listener())

function read() {
  try {
    return localStorage.getItem(STORAGE_KEY) === '1'
  } catch {
    return false
  }
}

export const wakeLockSupported = () => typeof navigator !== 'undefined' && 'wakeLock' in navigator

async function acquire() {
  if (!enabled || !wakeLockSupported() || document.visibilityState !== 'visible' || sentinel) return
  try {
    sentinel = await navigator.wakeLock.request('screen')
    sentinel.addEventListener('release', () => {
      sentinel = null
    })
  } catch {
    // Отказ (экономия батареи, вкладка не активна) — не ошибка для пользователя: просто экран погаснет как обычно.
    sentinel = null
  }
}

export function initWakeLock() {
  document.addEventListener('visibilitychange', () => void acquire())
  void acquire()
}

export async function setKeepAwake(next: boolean) {
  enabled = next
  try {
    localStorage.setItem(STORAGE_KEY, next ? '1' : '0')
  } catch {
    // Приватный режим — выбор не запомнится, но сейчас работает.
  }
  if (next) await acquire()
  else {
    await sentinel?.release()
    sentinel = null
  }
  emit()
}

export const useKeepAwake = () =>
  useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => enabled,
    () => false,
  )
