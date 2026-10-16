import { describe, expect, it } from 'vitest'
import { defaultEventTime, portionsCount, toEventItems } from './planning'

describe('planning', () => {
  it('keeps only positive whole portions', () => {
    const items = toEventItems({ a: '30', b: '', c: '0', d: '12' })
    expect(items).toEqual([
      { dishId: 'a', portions: 30 },
      { dishId: 'd', portions: 12 },
    ])
    expect(portionsCount(items)).toBe(42)
  })

  it('defaults to tomorrow evening', () => {
    expect(defaultEventTime(new Date(2026, 9, 31, 10, 0))).toBe('2026-11-01T19:00')
  })
})
