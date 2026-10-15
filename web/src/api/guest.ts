import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from './client'
import type { OrderStatus } from './types'

/** QR-меню: публичные запросы гостя (без входа) и настройки менеджера. */

export type GuestDishState = 'Available' | 'WithChanges' | 'Unavailable'
export type PaymentMethod = 'Sbp' | 'Cash'

export interface GuestIngredient {
  productId: string
  name: string
  removable: boolean
  /** Закончился: без замены или удаления блюдо не приготовить. */
  out: boolean
  /** Заменители, которых хватит на порцию. */
  substitutes: { productId: string; name: string }[]
}

export interface GuestDish {
  id: string
  name: string
  category: string | null
  price: number
  state: GuestDishState
  recommended: boolean
  ingredients: GuestIngredient[]
  suggestedChanges: { productId: string; kind: 'Remove' | 'Replace'; substituteId: string | null }[]
}

export interface GuestMenu {
  restaurant: string
  ordering: boolean
  tables: number
  maxPortions: number
  dishes: GuestDish[]
}

export interface GuestOrder {
  token: string
  number: string
  status: OrderStatus
  createdAt: string
  table: number | null
  payment: PaymentMethod
  total: number
  items: {
    dishName: string
    quantity: number
    amount: number
    modifications: { productName: string; kind: 'Remove' | 'Replace'; substituteName: string | null }[]
  }[]
}

export interface PlaceGuestOrder {
  items: { dishId: string; quantity: number; modifications?: { productId: string; kind: 'Remove' | 'Replace'; substituteId?: string }[] }[]
  table: number | null
  payment: PaymentMethod
}

export interface GuestMenuSettings {
  menuKey: string
  ordering: boolean
  tables: number
}

/** Меню без «живого» соединения: обновляем раз в 20 с и при возврате на вкладку — QR не нужно перепечатывать. */
const MENU_REFRESH_MS = 20_000
/** Статус заказа гостя — раз в 5 с, пока заказ не выдан или не отменён. */
const ORDER_REFRESH_MS = 5_000

const base = (key: string) => `/api/v1/public/menu/${encodeURIComponent(key)}`

export const useGuestMenu = (key: string) =>
  useQuery({
    queryKey: ['guest-menu', key],
    queryFn: ({ signal }) => api.get<GuestMenu>(base(key), { signal, anonymous: true }),
    refetchInterval: MENU_REFRESH_MS,
    refetchOnWindowFocus: true,
    // Неверный или перевыпущенный ключ (404) — сразу «меню не найдено», без повторов.
    retry: (count, error) => count < 2 && !(error instanceof ApiError && error.status === 404),
  })

export const useGuestOrder = (key: string, token: string) =>
  useQuery({
    queryKey: ['guest-order', key, token],
    queryFn: ({ signal }) => api.get<GuestOrder>(`${base(key)}/orders/${encodeURIComponent(token)}`, { signal, anonymous: true }),
    refetchInterval: (query) => (query.state.data && ['Served', 'Cancelled'].includes(query.state.data.status) ? false : ORDER_REFRESH_MS),
  })

export const usePlaceGuestOrder = (key: string) =>
  useMutation({
    mutationFn: ({ order, idempotencyKey }: { order: PlaceGuestOrder; idempotencyKey: string }) =>
      api.post<GuestOrder>(`${base(key)}/orders`, order, { anonymous: true, headers: { 'Idempotency-Key': idempotencyKey } }),
  })

export const useGuestMenuSettings = () =>
  useQuery({ queryKey: ['guest-menu-settings'], queryFn: ({ signal }) => api.get<GuestMenuSettings>('/api/v1/guest-menu', { signal }) })

export function useUpdateGuestMenuSettings() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (settings: { ordering: boolean; tables: number }) => api.put<GuestMenuSettings>('/api/v1/guest-menu', settings),
    onSuccess: (data) => client.setQueryData(['guest-menu-settings'], data),
  })
}

export function useRegenerateMenuKey() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.post<GuestMenuSettings>('/api/v1/guest-menu/regenerate'),
    onSuccess: (data) => client.setQueryData(['guest-menu-settings'], data),
  })
}
