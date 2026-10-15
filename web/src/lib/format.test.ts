import { beforeAll, describe, expect, it } from 'vitest'
import { createTestI18n } from '@/test/i18n'
import { createFormatter, offerVerdict, type Formatter } from './format'

// Среда, 14 октября 2026, 14:07 по Москве (11:07 UTC).
const NOW = new Date('2026-10-14T11:07:00Z')
const at = (moscowLocal: string) => new Date(`${moscowLocal}+03:00`)

let ru: Formatter
let en: Formatter

beforeAll(async () => {
  ru = createFormatter(await createTestI18n('ru'), () => NOW)
  en = createFormatter(await createTestI18n('en'), () => NOW)
})

describe('formatWhen (DESIGN.md §1.1)', () => {
  it.each([
    ['уже прошло', at('2026-10-14T13:00:00'), 'Critical', 'Уже испортилось', 'expired'],
    ['сегодня', at('2026-10-14T23:00:00'), 'Critical', 'Сегодня в 23:00', 'crit'],
    ['завтра', at('2026-10-15T08:00:00'), 'Warning', 'Завтра в 08:00', 'warn'],
    ['в ближайшие 6 дней', at('2026-10-17T12:00:00'), 'Warning', 'В субботу', 'warn'],
    ['в ближайшие 6 дней, свежее', at('2026-10-17T12:00:00'), 'Fresh', 'Годен до субботы', 'fresh'],
    ['дальше', at('2026-10-29T12:00:00'), 'Warning', '29 октября', 'warn'],
    ['дальше, свежее', at('2026-11-17T12:00:00'), 'Fresh', 'Годен до 17 ноября', 'fresh'],
  ] as const)('ru: %s', (_, expiresAt, status, text, tone) => {
    expect(ru.when(expiresAt, status)).toEqual({ text, tone })
  })

  it.each([
    [at('2026-10-14T23:00:00'), 'Critical', 'Today at 23:00'],
    [at('2026-10-15T08:00:00'), 'Warning', 'Tomorrow at 08:00'],
    [at('2026-10-17T12:00:00'), 'Fresh', 'Good until Saturday'],
    [at('2026-11-17T12:00:00'), 'Fresh', 'Good until November 17'],
    [at('2026-10-14T10:00:00'), 'Fresh', 'Already spoiled'],
  ] as const)('en: %s', (expiresAt, status, text) => {
    expect(en.when(expiresAt, status).text).toBe(text)
  })

  it('inline form starts lowercase', () => {
    expect(ru.whenInline(at('2026-10-14T23:00:00'))).toBe('сегодня в 23:00')
    expect(ru.whenInline(at('2026-10-17T12:00:00'))).toBe('в субботу')
    expect(en.whenInline(at('2026-10-15T08:00:00'))).toBe('tomorrow at 08:00')
  })

  it('days are counted by Moscow calendar, not by 24-hour blocks', () => {
    // 23:30 сегодня → 00:30 завтра: меньше часа, но уже «завтра».
    const late = createFormatter(ru.i18n, () => at('2026-10-14T23:30:00'))
    expect(late.when(at('2026-10-15T00:30:00'), 'Critical').text).toBe('Завтра в 00:30')
  })
})

describe('formatQty — как на кухне', () => {
  it.each([
    [2400, 'Gram', '2,4 кг', '2.4 kg'],
    [500, 'Gram', '500 г', '500 g'],
    [1800, 'Milliliter', '1,8 л', '1.8 L'],
    [750, 'Milliliter', '750 мл', '750 ml'],
    [90, 'Piece', '90 шт', '90 pcs'],
    [12500, 'Gram', '12,5 кг', '12.5 kg'],
    [1000, 'Gram', '1 кг', '1 kg'],
    [0.5, 'Piece', '0,5 шт', '0.5 pcs'],
  ] as const)('%s %s', (value, unit, expectedRu, expectedEn) => {
    expect(ru.qty(value, unit)).toBe(expectedRu)
    expect(en.qty(value, unit)).toBe(expectedEn)
  })

  it('thousands use a non-breaking group separator', () => {
    expect(ru.qty(1_250_000, 'Gram')).toBe('1250 кг')
    expect(ru.number(12500)).toBe('12\u00a0500')
  })
})

describe('точное количество — для заявок поставщику', () => {
  it('килограммы и литры до граммов, без округления до десятых', () => {
    expect(ru.qtyExact(1750, 'Gram')).toBe('1,75 кг')
    expect(ru.qtyExact(5250, 'Milliliter')).toBe('5,25 л')
    expect(ru.qtyExact(200, 'Gram')).toBe('200 г')
    expect(ru.qtyExact(60, 'Piece')).toBe('60 шт')
  })
})

describe('единицы не путаются', () => {
  it('штуки остаются штуками при любом количестве (в «кг» не переводятся)', () => {
    expect(ru.qty(1500, 'Piece').replace(/\s/g, ' ')).toBe('1 500 шт')
    expect(ru.qty(10, 'Piece')).toBe('10 шт')
    expect(en.qty(10, 'Piece')).toBe('10 pcs')
  })
})

describe('порции и прогноз', () => {
  it.each([
    [1, 'до 1 порции', 'хватит на 1 порцию'],
    [3, 'до 3 порций', 'хватит на 3 порции'],
    [16, 'до 16 порций', 'хватит на 16 порций'],
    [21, 'до 21 порции', 'хватит на 21 порцию'],
  ])('ru %i', (count, upTo, enough) => {
    expect(ru.portionsUpTo(count)).toBe(upTo)
    expect(ru.portionsEnoughFor(count)).toBe(enough)
  })

  it('en plurals', () => {
    expect(en.portionsUpTo(1)).toBe('up to 1 portion')
    expect(en.portionsEnoughFor(16)).toBe('enough for 16 portions')
  })

  it.each([
    [0.4, 'Меньше чем на час'],
    [1, 'Хватит примерно на 1 час'],
    [3.2, 'Хватит примерно на 3 часа'],
    [5, 'Хватит примерно на 5 часов'],
  ])('runway %f', (hours, text) => {
    expect(ru.runway(hours)).toBe(text)
  })
})

describe('остальное', () => {
  it('verdict by score', () => {
    expect(offerVerdict(80)).toBe('sellFirst')
    expect(offerVerdict(60)).toBe('sellFirst')
    expect(offerVerdict(54.8)).toBe('recommend')
    expect(offerVerdict(36.4)).toBe('canOffer')
    expect(ru.verdict(80)).toBe('Продавать в первую очередь')
    expect(en.verdict(40)).toBe('Worth offering')
  })

  it('today line', () => {
    expect(ru.todayLine()).toBe('Среда, 14 октября · сейчас 14:07')
    expect(en.todayLine()).toBe('Wednesday, October 14 · now 14:07')
  })
})

describe('F3: деньги, прошедшие моменты, даты, длительности', () => {
  it.each([
    [1440, '1 440 ₽'],
    [320, '320 ₽'],
    [1234.5, '1 235 ₽'],
  ])('money ru %s', (value, text) => {
    expect(ru.money(value).replace(/\s/g, ' ')).toBe(text)
  })

  it('money en', () => {
    expect(en.money(1440).replace(/\s/g, ' ')).toBe('RUB 1,440')
  })

  it.each([
    ['сегодня', at('2026-10-14T10:31:00'), 'сегодня в 10:31'],
    ['вчера', at('2026-10-13T22:05:00'), 'вчера в 22:05'],
    ['раньше', at('2026-10-09T08:00:00'), '9 октября в 08:00'],
  ] as const)('moment ru: %s', (_, value, text) => {
    expect(ru.moment(value)).toBe(text)
  })

  it('moment en', () => {
    expect(en.moment(at('2026-10-13T22:05:00'))).toBe('yesterday at 22:05')
    expect(en.moment(at('2026-10-09T08:00:00'))).toBe('October 9 at 08:00')
  })

  it('date — день и месяц по Москве', () => {
    expect(ru.date(new Date('2026-10-13T22:30:00Z'))).toBe('14 октября')
    expect(en.date(new Date('2026-10-13T22:30:00Z'))).toBe('October 14')
  })

  it.each([
    [0.4, 'меньше минуты'],
    [45, '45 мин'],
    [60, '1 ч'],
    [104.6, '1 ч 45 мин'],
    [1500, '1 д 1 ч'],
  ])('duration ru %s мин', (minutes, text) => {
    expect(ru.duration(minutes)).toBe(text)
  })

  it('duration en', () => {
    expect(en.duration(104.6)).toBe('1 h 45 min')
  })
})
