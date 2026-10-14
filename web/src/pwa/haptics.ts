/**
 * Тактильный отклик на телефоне (Android; iOS вибрацию из браузера не даёт — тихо пропускаем).
 * Не дублирует сообщение на экране, а подтверждает его «на ощупь» — повару с занятыми руками.
 */
const PATTERNS = {
  success: [18],
  error: [40, 60, 40],
} as const

export function vibrate(kind: keyof typeof PATTERNS) {
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
  if (typeof navigator.vibrate === 'function') navigator.vibrate([...PATTERNS[kind]])
}
