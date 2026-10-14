/** Ключи кэша TanStack Query: один источник для запросов и для обновлений из хаба. */
export const queryKeys = {
  stock: ['stock'] as const,
  batches: ['batches'] as const,
  menu: ['menu'] as const,
  stopList: ['stoplist'] as const,
  stopListForecast: ['stoplist', 'forecast'] as const,
  hotOffers: ['hot-offers'] as const,
  alerts: ['alerts'] as const,
  orders: ['orders'] as const,
  supplies: ['supplies'] as const,
  supplySuggestions: ['supply-suggestions'] as const,
  kpi: ['analytics', 'kpi'] as const,
  simulator: ['simulator'] as const,
}
