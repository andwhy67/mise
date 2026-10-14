import type { Product } from '@/api/types'
import type { ComboboxOption } from '@/components/Combobox'
import type { MeasureUnit } from './format'

/**
 * Продукты для выбора из списка: по категориям, по алфавиту; справа — единица учёта («кг», «л», «шт»),
 * чтобы штучный продукт нельзя было спутать с весовым.
 */
export function productOptions(products: Product[], noCategory: string, unitHint: (unit: MeasureUnit) => string): ComboboxOption[] {
  return [...products]
    .sort((a, b) => (a.category ?? noCategory).localeCompare(b.category ?? noCategory) || a.name.localeCompare(b.name))
    .map((p) => ({ value: p.id, label: p.name, group: p.category ?? noCategory, hint: unitHint(p.unit) }))
}
