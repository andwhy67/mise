/** RFC 7807 ProblemDetails, как его отдаёт API (+ произвольные расширения, например `missing`). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  traceId?: string
  [extension: string]: unknown
}

export class ApiError extends Error {
  readonly status: number
  readonly problem?: ProblemDetails

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

export interface ApiRequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
}

const baseUrl = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '')

export async function apiFetch<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const { body, headers, ...rest } = options
  const requestHeaders = new Headers(headers)
  requestHeaders.set('Accept', 'application/json, application/problem+json')
  if (body !== undefined) requestHeaders.set('Content-Type', 'application/json')

  let response: Response
  try {
    response = await fetch(`${baseUrl}${path}`, {
      ...rest,
      headers: requestHeaders,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') throw cause
    throw new ApiError(0, 'Нет связи с сервером')
  }

  if (!response.ok) throw await toApiError(response)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

async function toApiError(response: Response): Promise<ApiError> {
  const fallback = `Сервер вернул ошибку ${response.status}`
  const contentType = response.headers.get('Content-Type') ?? ''
  if (!contentType.includes('json')) return new ApiError(response.status, fallback)

  try {
    const problem = (await response.json()) as ProblemDetails
    return new ApiError(response.status, problem.title ?? problem.detail ?? fallback, problem)
  } catch {
    return new ApiError(response.status, fallback)
  }
}
