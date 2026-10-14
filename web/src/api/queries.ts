import { useQuery } from '@tanstack/react-query'
import { api } from './client'
import { queryKeys } from './keys'
import type {
  Alert,
  AlertSummary,
  AuthConfig,
  Batch,
  Dish,
  DishAvailability,
  HotOffers,
  Kpi,
  Order,
  OrderListItem,
  Paged,
  Product,
  ProductBatches,
  StockItem,
  StopListForecast,
  StopListHistoryItem,
  StopListItem,
  Supply,
  SupplyListItem,
  SupplySuggestions,
  User,
} from './types'

/** Запросы к API для экранов. Свежесть поддерживает хаб (инвалидация по событиям), фоновый refetch — страховка. */

export const useStopList = () =>
  useQuery({ queryKey: queryKeys.stopList, queryFn: ({ signal }) => api.get<StopListItem[]>('/api/v1/stoplist', { signal }) })

export const useStopListForecast = (horizonHours: number) =>
  useQuery({
    queryKey: [...queryKeys.stopListForecast, horizonHours],
    queryFn: ({ signal }) => api.get<StopListForecast>(`/api/v1/stoplist/forecast?horizonHours=${horizonHours}`, { signal }),
  })

export const useHotOffers = () =>
  useQuery({
    queryKey: [...queryKeys.hotOffers, 'default'],
    queryFn: ({ signal }) => api.get<HotOffers>('/api/v1/hot-offers', { signal }),
  })

/** Партии, истекающие в ближайшие N часов (включая уже истёкшие, но не списанные). */
export const useExpiringBatches = (withinHours: number) =>
  useQuery({
    queryKey: [...queryKeys.batches, 'expiring', withinHours],
    queryFn: ({ signal }) => api.get<Batch[]>(`/api/v1/batches?expiringWithinHours=${withinHours}`, { signal }),
  })

export const useKpi = (enabled = true) =>
  useQuery({ queryKey: queryKeys.kpi, queryFn: ({ signal }) => api.get<Kpi>('/api/v1/analytics/kpi', { signal }), enabled })

export const useSupplySuggestions = (enabled = true) =>
  useQuery({
    queryKey: queryKeys.supplySuggestions,
    queryFn: ({ signal }) => api.get<SupplySuggestions>('/api/v1/supply-suggestions', { signal }),
    enabled,
  })

export const useAuthConfig = () =>
  useQuery({
    queryKey: ['auth', 'config'],
    queryFn: ({ signal }) => api.get<AuthConfig>('/api/v1/auth/config', { signal, anonymous: true }),
    staleTime: Infinity,
  })

export const useStock = () =>
  useQuery({ queryKey: queryKeys.stock, queryFn: ({ signal }) => api.get<StockItem[]>('/api/v1/stock', { signal }) })

/** Поставки продукта в порядке списания — ключ под batches: события хаба про партии его обновляют. */
export const useProductBatches = (productId: string, enabled = true) =>
  useQuery({
    queryKey: [...queryKeys.batches, 'product', productId],
    queryFn: ({ signal }) => api.get<ProductBatches>(`/api/v1/stock/${productId}/batches`, { signal }),
    enabled,
  })

export const useMenu = () =>
  useQuery({ queryKey: queryKeys.menu, queryFn: ({ signal }) => api.get<DishAvailability[]>('/api/v1/menu/availability', { signal }) })

export const useStopListHistory = (pageSize = 30) =>
  useQuery({
    queryKey: [...queryKeys.stopList, 'history', pageSize],
    queryFn: ({ signal }) => api.get<Paged<StopListHistoryItem>>(`/api/v1/stoplist/history?pageSize=${pageSize}`, { signal }),
  })

export const useOrders = (pageSize = 20) =>
  useQuery({
    queryKey: [...queryKeys.orders, 'list', pageSize],
    queryFn: ({ signal }) => api.get<Paged<OrderListItem>>(`/api/v1/orders?pageSize=${pageSize}`, { signal }),
  })

export const useOrder = (id: string, enabled = true) =>
  useQuery({
    queryKey: [...queryKeys.orders, 'detail', id],
    queryFn: ({ signal }) => api.get<Order>(`/api/v1/orders/${id}`, { signal }),
    enabled,
  })

export type AlertFilter = 'attention' | 'all'

/** «Требуют внимания» — актуальные и не подтверждённые; «Все» — лента целиком. */
export const useAlerts = (filter: AlertFilter, pageSize: number) =>
  useQuery({
    queryKey: [...queryKeys.alerts, 'list', filter, pageSize],
    queryFn: ({ signal }) =>
      api.get<Paged<Alert>>(
        `/api/v1/alerts?pageSize=${pageSize}${filter === 'attention' ? '&acknowledged=false&active=true' : ''}`,
        { signal },
      ),
    placeholderData: (previous) => previous,
  })

export const useAlertSummary = () =>
  useQuery({
    queryKey: [...queryKeys.alerts, 'summary'],
    queryFn: ({ signal }) => api.get<AlertSummary>('/api/v1/alerts/summary', { signal }),
  })

export const useProducts = () =>
  useQuery({
    queryKey: ['products'],
    queryFn: ({ signal }) => api.get<Product[]>('/api/v1/products', { signal }),
    staleTime: 60_000,
  })

export const useSupplies = (pageSize = 20) =>
  useQuery({
    queryKey: [...queryKeys.supplies, 'list', pageSize],
    queryFn: ({ signal }) => api.get<Paged<SupplyListItem>>(`/api/v1/supplies?pageSize=${pageSize}`, { signal }),
  })

export const useSupply = (id: string, enabled = true) =>
  useQuery({
    queryKey: [...queryKeys.supplies, 'detail', id],
    queryFn: ({ signal }) => api.get<Supply>(`/api/v1/supplies/${id}`, { signal }),
    enabled,
  })

export const useDishes = () =>
  useQuery({ queryKey: ['dishes'], queryFn: ({ signal }) => api.get<Dish[]>('/api/v1/dishes', { signal }) })

export const useUsers = () => useQuery({ queryKey: ['users'], queryFn: ({ signal }) => api.get<User[]>('/api/v1/users', { signal }) })
