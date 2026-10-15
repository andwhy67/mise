import { describe, expect, it } from 'vitest'
import type { GuestDish } from '@/api/guest'
import { missingWithout } from '@/pages/Orders/cart'
import { qrCards } from '@/pages/QrMenu/qrCards'
import { cartLines, lineBlocked, tableFromQuery, toAvailability } from './guestMenu'

const dish = (overrides: Partial<GuestDish> = {}): GuestDish => ({
  id: 'd1',
  name: 'Лосось на гриле',
  category: 'Горячее',
  price: 890,
  state: 'WithChanges',
  recommended: false,
  ingredients: [
    { productId: 'salmon', name: 'Лосось', removable: false, out: false, substitutes: [] },
    { productId: 'lemon', name: 'Лимон', removable: true, out: true, substitutes: [] },
  ],
  suggestedChanges: [{ productId: 'lemon', kind: 'Remove', substituteId: null }],
  ...overrides,
})

describe('QR-меню гостя', () => {
  it('блюдо без остатков в граммах подходит общему окну «Изменить блюдо»: закончившееся надо убрать', () => {
    const availability = toAvailability(dish())
    expect(missingWithout(availability, [])).toEqual(['Лимон'])
    expect(missingWithout(availability, [{ productId: 'lemon', kind: 'Remove' }])).toEqual([])
    expect(availability.alternative?.changes[0]).toMatchObject({ productName: 'Лимон', kind: 'Remove' })
  })

  it('строка «как в меню» из стопа заблокирована, с изменениями — нет; пропавшие из меню блюда выпадают', () => {
    const menu = { restaurant: 'Бистро', ordering: true, tables: 10, maxPortions: 20, dishes: [dish()] }
    const lines = cartLines(new Map([['d1', 1], ['d1|-lemon', 2], ['gone', 1]]), menu)
    expect(lines.map((l) => [l.key, lineBlocked(l)])).toEqual([
      ['d1', true],
      ['d1|-lemon', false],
    ])
  })

  it('стол из QR — только целый номер в пределах столов ресторана', () => {
    expect(tableFromQuery('5', 10)).toBe(5)
    expect(tableFromQuery('11', 10)).toBeNull()
    expect(tableFromQuery('0', 10)).toBeNull()
    expect(tableFromQuery('2a', 10)).toBeNull()
    expect(tableFromQuery(null, 10)).toBeNull()
  })

  it('карточки для печати: общий QR и по одной на стол', () => {
    expect(qrCards('https://mise.app/m/abc', 2)).toEqual([
      { table: null, url: 'https://mise.app/m/abc' },
      { table: 1, url: 'https://mise.app/m/abc?t=1' },
      { table: 2, url: 'https://mise.app/m/abc?t=2' },
    ])
  })
})
