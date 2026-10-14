import type { i18n as I18n } from 'i18next'

/**
 * Единственное место, где данные API превращаются в слова (DESIGN.md §1.1, §9a).
 * Время показывается по Москве, числа и множественные формы — по правилам языка интерфейса.
 * Компоненты сами ничего не форматируют.
 */
export const DISPLAY_TIME_ZONE = 'Europe/Moscow'

export type ShelfStatus = 'Fresh' | 'Warning' | 'Critical' | 'Expired'
export type Tone = 'fresh' | 'warn' | 'crit' | 'expired' | 'info'
export type MeasureUnit = 'Gram' | 'Milliliter' | 'Piece'
export type OfferVerdict = 'sellFirst' | 'recommend' | 'canOffer'

/** Пороги вердикта горячих предложений по score (настраиваются здесь). */
export const OFFER_THRESHOLDS = { sellFirst: 60, recommend: 40 } as const

export function offerVerdict(score: number): OfferVerdict {
  if (score >= OFFER_THRESHOLDS.sellFirst) return 'sellFirst'
  if (score >= OFFER_THRESHOLDS.recommend) return 'recommend'
  return 'canOffer'
}

export const VERDICT_TONE: Record<OfferVerdict, Tone> = { sellFirst: 'crit', recommend: 'warn', canOffer: 'info' }

const STATUS_TONE: Record<ShelfStatus, Tone> = { Fresh: 'fresh', Warning: 'warn', Critical: 'crit', Expired: 'expired' }

const HOUR_MS = 3_600_000
const DAY_MS = 24 * HOUR_MS

export interface When {
  text: string
  tone: Tone
}

export interface Formatter {
  readonly i18n: I18n
  readonly locale: string
  when(expiresAt: Date | string, status: ShelfStatus): When
  /** Момент внутри предложения: «испортится сегодня в 23:00», «… в субботу» (без «Годен до»). */
  whenInline(expiresAt: Date | string): string
  time(value: Date | string): string
  number(value: number, maxFractionDigits?: number): string
  qty(value: number, unit: MeasureUnit): string
  portionsUpTo(count: number): string
  portionsEnoughFor(count: number): string
  runway(hours: number): string
  verdict(score: number): string
  todayLine(): string
  /** Цена: «1 440 ₽» без копеек. */
  money(value: number): string
  /** Когда это случилось: «сегодня в 22:31», «вчера в 10:00», «9 октября в 08:00». */
  moment(value: Date | string): string
  /** День по Москве: «14 октября». */
  date(value: Date | string): string
  /** Длительность: «45 мин», «1 ч 45 мин», «1 д 1 ч». */
  duration(minutes: number): string
}

/** Форматер под текущий язык i18n; `now` — функция, чтобы тесты и экран жили по одним часам. */
export function createFormatter(i18n: I18n, now: () => Date = () => new Date()): Formatter {
  const t = i18n.getFixedT(i18n.language, 'format')
  const locale = i18n.language === 'en' ? 'en-US' : i18n.language
  const timeFormat = new Intl.DateTimeFormat(locale, { timeZone: DISPLAY_TIME_ZONE, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
  const dateFormat = new Intl.DateTimeFormat(locale, { timeZone: DISPLAY_TIME_ZONE, day: 'numeric', month: 'long' })
  const weekdayFormat = new Intl.DateTimeFormat(locale, { timeZone: DISPLAY_TIME_ZONE, weekday: 'long' })
  const dayKey = new Intl.DateTimeFormat('en-CA', { timeZone: DISPLAY_TIME_ZONE, year: 'numeric', month: '2-digit', day: '2-digit' })
  const weekdayIndex = new Intl.DateTimeFormat('en-US', { timeZone: DISPLAY_TIME_ZONE, weekday: 'short' })
  const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

  const toDate = (value: Date | string) => (typeof value === 'string' ? new Date(value) : value)
  /** Разница в календарных днях по Москве. */
  const calendarDays = (from: Date, to: Date) => (Date.parse(dayKey.format(to)) - Date.parse(dayKey.format(from))) / DAY_MS
  const capitalize = (s: string) => s.charAt(0).toLocaleUpperCase(locale) + s.slice(1)

  const number = (value: number, maxFractionDigits = 1) =>
    new Intl.NumberFormat(locale, { maximumFractionDigits: maxFractionDigits }).format(value)

  return {
    i18n,
    locale,

    when(expiresAtValue, status) {
      const expiresAt = toDate(expiresAtValue)
      const current = now()
      if (expiresAt.getTime() <= current.getTime() || status === 'Expired') return { text: t('when.expired'), tone: 'expired' }

      const tone = STATUS_TONE[status]
      const days = calendarDays(current, expiresAt)
      const weekday = WEEKDAYS.indexOf(weekdayIndex.format(expiresAt))
      if (status === 'Fresh' && days > 1) {
        const text = days <= 6 ? t(`when.goodUntilWeekday.${weekday}`) : t('when.goodUntilDate', { date: dateFormat.format(expiresAt) })
        return { text, tone }
      }
      if (days <= 0) return { text: t('when.today', { time: timeFormat.format(expiresAt) }), tone }
      if (days === 1) return { text: t('when.tomorrow', { time: timeFormat.format(expiresAt) }), tone }
      if (days <= 6) return { text: t(`when.on.${weekday}`), tone }
      return { text: capitalize(dateFormat.format(expiresAt)), tone }
    },

    whenInline(expiresAtValue) {
      // Статус влияет только на цвет; в предложении нужен момент, а не «годен до …».
      const { text } = this.when(expiresAtValue, 'Warning')
      return text.charAt(0).toLocaleLowerCase(locale) + text.slice(1)
    },

    time: (value) => timeFormat.format(toDate(value)),

    number,

    qty(value, unit) {
      if (unit === 'Piece') return `${number(value)} ${t('unit.pcs')}`
      const big = Math.abs(value) >= 1000
      const unitKey = unit === 'Gram' ? (big ? 'kg' : 'g') : big ? 'l' : 'ml'
      const shown = big ? value / 1000 : value
      // Килограммы и литры — с одним знаком, граммы и миллилитры — целые; без разделителя тысяч в мере.
      const formatted = new Intl.NumberFormat(locale, { maximumFractionDigits: big ? 1 : 0, useGrouping: false }).format(shown)
      return `${formatted} ${t(`unit.${unitKey}`)}`
    },

    portionsUpTo: (count) => t('portions.upTo', { count }),
    portionsEnoughFor: (count) => t('portions.enoughFor', { count }),

    runway(hours) {
      if (hours < 1) return t('runway.lessThanHour')
      return t('runway.hours', { count: Math.floor(hours) })
    },

    verdict: (score) => t(`verdict.${offerVerdict(score)}`),

    money: (value) => new Intl.NumberFormat(locale, { style: 'currency', currency: 'RUB', maximumFractionDigits: 0 }).format(value),

    moment(value) {
      const date = toDate(value)
      const days = calendarDays(date, now())
      const time = timeFormat.format(date)
      if (days === 0) return t('moment.today', { time })
      if (days === 1) return t('moment.yesterday', { time })
      if (days === -1) return t('moment.tomorrow', { time })
      return t('moment.date', { date: dateFormat.format(date), time })
    },

    date: (value) => dateFormat.format(toDate(value)),

    duration(minutes) {
      if (minutes < 1) return t('duration.lessThanMinute')
      const total = Math.round(minutes)
      const days = Math.floor(total / 1440)
      const hours = Math.floor((total % 1440) / 60)
      const mins = total % 60
      // Две старшие единицы достаточно: «1 д 1 ч», а не «1 д 1 ч 0 мин».
      const parts = [
        days ? t('duration.d', { count: days }) : '',
        hours ? t('duration.h', { count: hours }) : '',
        mins && !days ? t('duration.min', { count: mins }) : '',
      ].filter(Boolean)
      return parts.join(' ')
    },

    todayLine() {
      const current = now()
      return t('todayLine', {
        weekday: capitalize(weekdayFormat.format(current)),
        date: dateFormat.format(current),
        time: timeFormat.format(current),
      })
    },
  }
}

/** Миллисекунды → часы (для прогнозов «хватит примерно на …»). */
export const hoursBetween = (from: Date, to: Date) => (to.getTime() - from.getTime()) / HOUR_MS
