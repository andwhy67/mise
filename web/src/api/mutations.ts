import { useMutation, useQueryClient, type QueryKey } from '@tanstack/react-query'
import { api } from './client'
import { queryKeys } from './keys'
import type { Order, OrderStatus, Supply, SupplyRequest, WriteOffRequest } from './types'

/**
 * Изменения данных. После успеха сразу инвалидируем затронутое: хаб тоже пришлёт событие,
 * но экран того, кто нажал кнопку, не должен ждать сеть по кругу.
 */
function useInvalidate() {
  const client = useQueryClient()
  return (...keys: QueryKey[]) => Promise.all(keys.map((queryKey) => client.invalidateQueries({ queryKey })))
}

const STOCK_EFFECTS: QueryKey[] = [queryKeys.stock, queryKeys.batches, queryKeys.menu, queryKeys.stopList, queryKeys.hotOffers, queryKeys.kpi]

export function useWriteOff() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ batchId, ...body }: WriteOffRequest & { batchId: string }) => api.post(`/api/v1/batches/${batchId}/write-off`, body),
    onSuccess: () => invalidate(...STOCK_EFFECTS, queryKeys.alerts),
  })
}

export interface OrderLine {
  dishId: string
  quantity: number
}

/** Замена с согласия повара: весь productId заказа — из substituteId (по правилу справочника). */
export interface OrderSubstitution {
  productId: string
  substituteId: string
}

/** Заказ с Idempotency-Key: повторное нажатие или обрыв связи не создаст второй заказ. */
export function useCreateOrder() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ items, substitutions, idempotencyKey }: { items: OrderLine[]; substitutions?: OrderSubstitution[]; idempotencyKey: string }) =>
      api.post<Order>('/api/v1/orders', { items, substitutions }, { headers: { 'Idempotency-Key': idempotencyKey } }),
    onSuccess: () => invalidate(...STOCK_EFFECTS, queryKeys.orders),
  })
}

export function useOrderStatus() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: OrderStatus }) => api.patch<Order>(`/api/v1/orders/${id}/status`, { status }),
    onSuccess: () => invalidate(queryKeys.orders),
  })
}

export function useCancelOrder() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (id: string) => api.post(`/api/v1/orders/${id}/cancel`),
    onSuccess: () => invalidate(...STOCK_EFFECTS, queryKeys.orders),
  })
}

export function useAckAlert() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (id: string) => api.post(`/api/v1/alerts/${id}/ack`),
    onSuccess: () => invalidate(queryKeys.alerts),
  })
}

/** Вернуть блюдо в меню / убрать вручную (менеджер): PUT принимает карточку целиком, поэтому сначала читаем её. */
export function useSetDishActive() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: async ({ dishId, isActive }: { dishId: string; isActive: boolean }) => {
      const dish = await api.get<{ name: string; category: string | null; price: number }>(`/api/v1/dishes/${dishId}`)
      return api.put(`/api/v1/dishes/${dishId}`, { name: dish.name, category: dish.category, price: dish.price, isActive })
    },
    onSuccess: () => invalidate(queryKeys.menu, queryKeys.stopList, queryKeys.hotOffers, queryKeys.kpi),
  })
}

/** Приём поставки с Idempotency-Key: повторная отправка той же формы не создаст вторую поставку. */
export function useCreateSupply() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ body, idempotencyKey }: { body: unknown; idempotencyKey: string }) =>
      api.post<Supply>('/api/v1/supplies', body, { headers: { 'Idempotency-Key': idempotencyKey } }),
    onSuccess: () => invalidate(...STOCK_EFFECTS, queryKeys.supplies, queryKeys.supplySuggestions, queryKeys.alerts),
  })
}

/** Заявка поставщику: номер и запись в журнале; остатки не меняет. */
export function useCreateSupplyRequest() {
  return useMutation({
    mutationFn: (body: { supplier: string | null; lines: { productId: string; quantity: number }[] }) =>
      api.post<SupplyRequest>('/api/v1/supply-requests', body),
  })
}

/** Справочники: после изменения продукта или блюда пересчитываются меню, стоп-лист и предложения. */
const CATALOG_EFFECTS: QueryKey[] = [['products'], ['dishes'], queryKeys.stock, queryKeys.menu, queryKeys.stopList, queryKeys.hotOffers]

export function useSaveProduct() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: unknown }) => (id ? api.put(`/api/v1/products/${id}`, body) : api.post('/api/v1/products', body)),
    onSuccess: () => invalidate(...CATALOG_EFFECTS),
  })
}

export function useDeleteProduct() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: string) => api.delete(`/api/v1/products/${id}`), onSuccess: () => invalidate(...CATALOG_EFFECTS) })
}

/** Блюдо: у существующего карточка и состав меняются разными запросами (PUT /dishes/{id} и /ingredients). */
export function useSaveDish() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: { ingredients: unknown[] } & Record<string, unknown> }) => {
      if (!id) return api.post('/api/v1/dishes', body)
      const { ingredients, ...card } = body
      await api.put(`/api/v1/dishes/${id}`, card)
      return api.put(`/api/v1/dishes/${id}/ingredients`, { ingredients })
    },
    onSuccess: () => invalidate(...CATALOG_EFFECTS),
  })
}

export function useDeleteDish() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: string) => api.delete(`/api/v1/dishes/${id}`), onSuccess: () => invalidate(...CATALOG_EFFECTS) })
}
