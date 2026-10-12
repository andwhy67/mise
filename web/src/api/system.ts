import { useQuery } from '@tanstack/react-query'
import { apiFetch } from './client'

export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy'

export interface HealthReport {
  status: HealthStatus
  totalDurationMs: number
  checks: Record<string, { status: HealthStatus; durationMs: number; error?: string | null }>
}

export function useHealth() {
  return useQuery({
    queryKey: ['health'],
    queryFn: ({ signal }) => apiFetch<HealthReport>('/health', { signal }),
    refetchInterval: 30_000,
  })
}
