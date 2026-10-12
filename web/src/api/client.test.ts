import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiFetch } from './client'

function mockFetch(response: Response) {
  const fn = vi.fn<typeof fetch>().mockResolvedValue(response)
  vi.stubGlobal('fetch', fn)
  return fn
}

describe('apiFetch', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('возвращает распарсенный JSON при успехе', async () => {
    mockFetch(Response.json({ status: 'ok' }))

    await expect(apiFetch<{ status: string }>('/api/v1/ping')).resolves.toEqual({ status: 'ok' })
  })

  it('отправляет JSON-тело и заголовки', async () => {
    const fn = mockFetch(Response.json({}))

    await apiFetch('/api/v1/orders', { method: 'POST', body: { items: [] }, headers: { 'Idempotency-Key': 'k1' } })

    const [, init] = fn.mock.calls[0]!
    const headers = new Headers(init?.headers)
    expect(init?.method).toBe('POST')
    expect(init?.body).toBe('{"items":[]}')
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(headers.get('Idempotency-Key')).toBe('k1')
  })

  it('превращает ProblemDetails в ApiError с деталями', async () => {
    const problem = {
      type: 'about:blank',
      title: 'Недостаточно ингредиентов',
      status: 422,
      detail: 'Не хватает сливок',
      missing: [{ productName: 'Сливки 33%', required: 300, available: 120 }],
    }
    mockFetch(new Response(JSON.stringify(problem), { status: 422, headers: { 'Content-Type': 'application/problem+json' } }))

    const error = await apiFetch('/api/v1/orders').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    const apiError = error as ApiError
    expect(apiError.status).toBe(422)
    expect(apiError.message).toBe('Недостаточно ингредиентов')
    expect(apiError.problem?.detail).toBe('Не хватает сливок')
    expect(apiError.problem?.['missing']).toHaveLength(1)
  })

  it('даёт человеческое сообщение, если тело ошибки не JSON', async () => {
    mockFetch(new Response('Bad gateway', { status: 502 }))

    const error = (await apiFetch('/health').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(502)
    expect(error.problem).toBeUndefined()
    expect(error.message).toBe('Сервер вернул ошибку 502')
  })

  it('сетевую ошибку оборачивает в ApiError со статусом 0', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch')))

    const error = (await apiFetch('/health').catch((e: unknown) => e)) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(0)
    expect(error.message).toBe('Нет связи с сервером')
  })
})
