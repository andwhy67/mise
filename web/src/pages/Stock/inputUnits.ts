import type { MeasureUnit } from '@/lib/format'

/** Единицы ввода: на кухне считают и в граммах, и в килограммах (API пересчитает в базовую). */
export const INPUT_UNITS: Record<MeasureUnit, { value: string; factor: number; key: string }[]> = {
  Gram: [
    { value: 'Kilogram', factor: 1000, key: 'kg' },
    { value: 'Gram', factor: 1, key: 'g' },
  ],
  Milliliter: [
    { value: 'Liter', factor: 1000, key: 'l' },
    { value: 'Milliliter', factor: 1, key: 'ml' },
  ],
  Piece: [{ value: 'Piece', factor: 1, key: 'pcs' }],
}
