import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { MeasureUnit } from '@/lib/format'
import { api } from './client'
import { queryKeys } from './keys'

/** Заготовка и сколько её можно приготовить из пригодных остатков. */
export interface PrepItem {
  id: string
  name: string
  unit: MeasureUnit
  yield: number
  shelfLifeHours: number
  usable: number
  maxQuantity: number
}

export interface PrepBatchUse {
  batchId: string
  quantity: number
  expiresAt: string
  /** Партия на грани срока: в заготовке она спасена, а не списана. */
  rescued: boolean
}

export interface PrepIngredientUse {
  productId: string
  productName: string
  unit: MeasureUnit
  required: number
  available: number
  shortage: number
  batches: PrepBatchUse[]
}

export interface PrepPlan {
  prepId: string
  prepName: string
  unit: MeasureUnit
  quantity: number
  canCook: boolean
  expiresAt: string
  shelfLifeHours: number
  /** Продукт, чей срок укоротил заготовку; null — заготовка живёт свой полный срок. */
  limitingProductName: string | null
  cost: number | null
  rescuedCost: number
  ingredients: PrepIngredientUse[]
}

export interface PrepResult {
  supplyId: string
  number: string
  batchId: string
  plan: PrepPlan
}

export interface CookPrepRequest {
  quantity: number
  unit?: string
}

export const usePreps = (enabled = true) =>
  useQuery({ queryKey: queryKeys.preps, queryFn: ({ signal }) => api.get<PrepItem[]>('/api/v1/preps', { signal }), enabled })

/** Предпросмотр: какие партии уйдут и какой будет срок. Пока вводят число, показываем прошлый расчёт. */
export const usePrepPreview = (prepId: string, request: CookPrepRequest | null) =>
  useQuery({
    queryKey: [...queryKeys.preps, prepId, 'preview', request],
    queryFn: ({ signal }) => api.post<PrepPlan>(`/api/v1/preps/${prepId}/preview`, request, { signal }),
    enabled: request !== null,
    placeholderData: keepPreviousData,
  })

/** Готовка с Idempotency-Key: повторное нажатие не спишет продукты дважды. */
export function useCookPrep() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ prepId, idempotencyKey, ...body }: CookPrepRequest & { prepId: string; idempotencyKey: string }) =>
      api.post<PrepResult>(`/api/v1/preps/${prepId}/cook`, body, { headers: { 'Idempotency-Key': idempotencyKey } }),
    onSuccess: () =>
      Promise.all(
        [queryKeys.preps, queryKeys.stock, queryKeys.batches, queryKeys.menu, queryKeys.stopList, queryKeys.hotOffers, queryKeys.kpi, queryKeys.supplies].map(
          (queryKey) => client.invalidateQueries({ queryKey }),
        ),
      ),
  })
}
