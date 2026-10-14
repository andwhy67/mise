import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { createFormatter, type Formatter } from './format'

/** Текущее время, обновляется раз в минуту: «Сегодня в 23:00» превращается в «Уже испортилось» само. */
export function useNow(intervalMs = 60_000): Date {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs])
  return now
}

/** Форматер под язык интерфейса; пересоздаётся при смене языка и каждую минуту. */
export function useFormat(): Formatter {
  const { i18n } = useTranslation('format')
  const language = i18n.language
  const now = useNow()
  // language — явная зависимость: объект i18n тот же, а словари и локаль чисел/дат уже другие.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useMemo(() => createFormatter(i18n, () => now), [i18n, language, now])
}
