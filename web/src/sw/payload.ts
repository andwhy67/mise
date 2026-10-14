/** Пуш от сервера (Mise.Infrastructure/Push): текст уже на языке подписчика. */
export interface PushPayload {
  title: string
  body: string
  /** Раздел, который открыть по клику: /stoplist, /stock?show=spoiled… */
  url: string
  /** Одинаковый tag заменяет предыдущее уведомление (не копим десять «Шпинат скоро испортится»). */
  tag: string
  severity: 'Info' | 'Warning' | 'Critical'
  lang: string
}
