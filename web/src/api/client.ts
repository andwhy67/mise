import { session } from '@/auth/session'
import { ApiError, type ProblemDetails } from './errors'

export { ApiError, type ProblemDetails }

export interface ApiRequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
  /** Не подставлять токен и не обновлять сессию при 401 (вход, обновление, выход). */
  anonymous?: boolean
}

const baseUrl = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '')

/**
 * Запрос к API: JSON, Bearer-токен из памяти. На 401 один раз обновляет сессию по refresh-cookie
 * и повторяет запрос; если обновить не удалось — сессия завершается (экран входа).
 */
export async function apiFetch<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const response = await send(path, options)
  if (response.status === 401 && !options.anonymous && session.isSignedIn()) {
    if (await session.refresh()) return parse<T>(await send(path, options))
    session.clear('expired')
  }
  return parse<T>(response)
}

async function send(path: string, { body, headers, anonymous, ...rest }: ApiRequestOptions): Promise<Response> {
  const requestHeaders = new Headers(headers)
  requestHeaders.set('Accept', 'application/json, application/problem+json')
  if (body !== undefined) requestHeaders.set('Content-Type', 'application/json')
  const token = anonymous ? null : session.accessToken()
  if (token) requestHeaders.set('Authorization', `Bearer ${token}`)

  try {
    return await fetch(`${baseUrl}${path}`, {
      credentials: 'same-origin',
      ...rest,
      headers: requestHeaders,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') throw cause
    throw new ApiError(0, 'Network error')
  }
}

async function parse<T>(response: Response): Promise<T> {
  if (!response.ok) throw await toApiError(response)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

async function toApiError(response: Response): Promise<ApiError> {
  const fallback = `HTTP ${response.status}`
  const contentType = response.headers.get('Content-Type') ?? ''
  if (!contentType.includes('json')) return new ApiError(response.status, fallback)
  try {
    const problem = (await response.json()) as ProblemDetails
    return new ApiError(response.status, problem.detail ?? problem.title ?? fallback, problem)
  } catch {
    return new ApiError(response.status, fallback)
  }
}

export const api = {
  get: <T>(path: string, init?: ApiRequestOptions) => apiFetch<T>(path, { ...init, method: 'GET' }),
  post: <T>(path: string, body?: unknown, init?: ApiRequestOptions) => apiFetch<T>(path, { ...init, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, init?: ApiRequestOptions) => apiFetch<T>(path, { ...init, method: 'PUT', body }),
  patch: <T>(path: string, body?: unknown, init?: ApiRequestOptions) => apiFetch<T>(path, { ...init, method: 'PATCH', body }),
  delete: <T>(path: string, init?: ApiRequestOptions) => apiFetch<T>(path, { ...init, method: 'DELETE' }),
}
