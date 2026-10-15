import { describe, expect, it } from 'vitest'
import { byColumn, formatTimer, lateness, newlyCreated, type KitchenOrder } from './kitchen'

const order = (id: string, status: KitchenOrder['status'], statusSince: string): KitchenOrder => ({
  id,
  number: `ORD-${id}`,
  status,
  createdAt: '2026-10-15T10:00:00Z',
  statusSince,
  portions: 1,
  items: [{ dishName: 'Паста', quantity: 1 }],
  substitutions: [],
})

describe('экран кухни', () => {
  it('по колонкам; внутри — кто дольше ждёт, тот выше', () => {
    const columns = byColumn([
      order('b', 'Created', '2026-10-15T10:05:00Z'),
      order('a', 'Created', '2026-10-15T10:01:00Z'),
      order('c', 'Done', '2026-10-15T10:02:00Z'),
    ])
    expect(columns.Created.map((o) => o.id)).toEqual(['a', 'b'])
    expect(columns.Cooking).toEqual([])
    expect(columns.Done.map((o) => o.id)).toEqual(['c'])
  })

  it('таймер — минуты:секунды, после часа — с часами', () => {
    expect(formatTimer(0)).toBe('0:00')
    expect(formatTimer(247_000)).toBe('4:07')
    expect(formatTimer(3_847_000)).toBe('1:04:07')
  })

  it('пороги свои у каждой колонки: готовое на раздаче «остывает» быстрее', () => {
    const now = new Date('2026-10-15T10:06:00Z')
    expect(lateness(order('x', 'Created', '2026-10-15T10:00:00Z'), now)).toBe('warn')
    expect(lateness(order('x', 'Cooking', '2026-10-15T10:00:00Z'), now)).toBe('fresh')
    expect(lateness(order('x', 'Done', '2026-10-15T09:57:00Z'), now)).toBe('crit')
  })

  it('звонок — только на новые принятые, и не при первой загрузке', () => {
    const orders = [order('a', 'Created', 'x'), order('b', 'Cooking', 'x'), order('c', 'Created', 'x')]
    expect(newlyCreated(null, orders)).toEqual([])
    expect(newlyCreated(new Set(['a', 'b']), orders)).toEqual(['c'])
    expect(newlyCreated(new Set(['a']), [order('b', 'Cooking', 'x')])).toEqual([])
  })
})
