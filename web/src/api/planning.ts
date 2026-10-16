import { useMutation } from '@tanstack/react-query'
import type { MeasureUnit } from '@/lib/format'
import { api } from './client'

export interface EventItem {
  dishId: string
  portions: number
}

export interface EventLine {
  productId: string
  productName: string
  unit: MeasureUnit
  need: number
  /** Сколько останется к началу события после обычных гостей и истечения партий. */
  availableAtEvent: number
  shortage: number
  /** Нехватка, округлённая вверх до фасовки поставщика. */
  buyQty: number
  supplier: string | null
  /** Сколько событие заберёт из того, что без него истекло бы. */
  rescuedQty: number
  rescuedCost: number | null
}

export interface EventPlan {
  generatedAt: string
  at: string
  feasible: boolean
  revenue: number
  rescuedCost: number
  dishes: { dishId: string; name: string; portions: number; price: number }[]
  lines: EventLine[]
  stopsAfter: { dishId: string; name: string; portionsBefore: number }[]
}

/** «Что если»: расчёт без изменений на складе — поэтому мутация по кнопке, а не запрос в кэше. */
export const usePlanEvent = () =>
  useMutation({
    mutationFn: (body: { items: EventItem[]; at: string }) => api.post<EventPlan>('/api/v1/planning/event', body),
  })
