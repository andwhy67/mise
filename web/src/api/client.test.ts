import { afterEach, describe, expect, it, vi } from 'vitest'
import { session } from '@/auth/session'
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
    expect(apiError.message).toBe('Не хватает сливок')
    expect(apiError.problem?.detail).toBe('Не хватает сливок')
    expect(apiError.problem?.['missing']).toHaveLength(1)
  })

  it('даёт человеческое сообщение, если тело ошибки не JSON', async () => {
    mockFetch(new Response('Bad gateway', { status: 502 }))

    const error = (await apiFetch('/health').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(502)
    expect(error.problem).toBeUndefined()
    expect(error.message).toBe('HTTP 502')
  })

  it('сетевую ошибку оборачивает в ApiError со статусом 0', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch')))

    const error = (await apiFetch('/health').catch((e: unknown) => e)) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(0)
    expect(error.code).toBe('network')
  })
})

describe('сессия и 401', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    session.clear(null)
  })

  const login = (token: string) =>
    Response.json({ accessToken: token, expiresAt: new Date(Date.now() + 900_000).toISOString(), user: { id: '1', login: 'chef', displayName: 'Шеф', role: 'Chef' } })

  it('подставляет Bearer-токен после входа', async () => {
    const fn = vi.fn<typeof fetch>().mockResolvedValueOnce(login('t1')).mockResolvedValueOnce(Response.json([]))
    vi.stubGlobal('fetch', fn)

    await session.login('chef', 'pass-1234')
    await apiFetch('/api/v1/stock')

    expect(new Headers(fn.mock.calls[1]![1]?.headers).get('Authorization')).toBe('Bearer t1')
  })

  it('на 401 один раз обновляет сессию и повторяет запрос', async () => {
    const fn = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(login('old'))
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(login('new'))
      .mockResolvedValueOnce(Response.json({ ok: true }))
    vi.stubGlobal('fetch', fn)
    await session.login('chef', 'pass-1234')

    await expect(apiFetch('/api/v1/stock')).resolves.toEqual({ ok: true })

    expect(String(fn.mock.calls[2]![0])).toContain('/api/v1/auth/refresh')
    expect(new Headers(fn.mock.calls[3]![1]?.headers).get('Authorization')).toBe('Bearer new')
  })

  it('если обновить не удалось — сессия завершается', async () => {
    const fn = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(login('old'))
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
    vi.stubGlobal('fetch', fn)
    await session.login('chef', 'pass-1234')

    await expect(apiFetch('/api/v1/stock')).rejects.toBeInstanceOf(ApiError)

    expect(session.snapshot()).toMatchObject({ status: 'signedOut', reason: 'expired' })
  })

  it('ошибка входа приходит как ApiError с кодом', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify({ status: 401, code: 'auth.invalid_credentials', detail: 'x' }), {
        status: 401, headers: { 'Content-Type': 'application/problem+json' } })))

    const error = (await session.login('chef', 'bad').catch((e: unknown) => e)) as ApiError

    expect(error.code).toBe('auth.invalid_credentials')
    expect(session.isSignedIn()).toBe(false)
  })
})
