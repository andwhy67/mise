import { useSyncExternalStore } from 'react'
import { ApiError } from '@/api/errors'

export type UserRole = 'Warehouse' | 'Chef' | 'Manager' | 'Simulator'

export interface SessionUser {
  id: string
  login: string
  displayName: string
  role: UserRole
}

interface LoginResponse {
  accessToken: string
  expiresAt: string
  user: SessionUser
}

export type SessionStatus = 'unknown' | 'signedIn' | 'signedOut'
export type SignOutReason = 'logout' | 'expired' | null

interface State {
  status: SessionStatus
  user: SessionUser | null
  reason: SignOutReason
}

const AUTH = '/api/v1/auth'
const baseUrl = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '')

/**
 * Сессия: access-токен живёт только в памяти (не в localStorage — недоступен XSS-скриптам после перезагрузки),
 * refresh-токен — в httpOnly-cookie, его видит только сервер. При старте сессия восстанавливается по cookie.
 */
function createSession() {
  let token: string | null = null
  let tokenExpiresAt = 0
  let state: State = { status: 'unknown', user: null, reason: null }
  let refreshing: Promise<boolean> | null = null
  const listeners = new Set<() => void>()

  const set = (next: State) => {
    state = next
    listeners.forEach((listener) => listener())
  }

  const accept = (response: LoginResponse) => {
    token = response.accessToken
    tokenExpiresAt = Date.parse(response.expiresAt)
    set({ status: 'signedIn', user: response.user, reason: null })
  }

  async function post(path: string, body?: unknown): Promise<Response> {
    return fetch(`${baseUrl}${AUTH}${path}`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: body === undefined ? { Accept: 'application/json' } : { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  }

  async function signIn(path: string, body: unknown): Promise<void> {
    const response = await post(path, body)
    if (!response.ok) {
      const problem = response.headers.get('Content-Type')?.includes('json') ? await response.json() : undefined
      throw new ApiError(response.status, problem?.detail ?? `HTTP ${response.status}`, problem)
    }
    accept((await response.json()) as LoginResponse)
  }

  return {
    subscribe(listener: () => void) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    snapshot: () => state,
    isSignedIn: () => state.status === 'signedIn',
    accessToken: () => token,
    /** Токен для SignalR: заранее обновляет истекающий. */
    async freshToken(): Promise<string | null> {
      if (token && Date.now() < tokenExpiresAt - 30_000) return token
      return (await this.refresh()) ? token : null
    },

    login: (login: string, password: string) => signIn('/login', { login, password }),
    demoLogin: (role: Exclude<UserRole, 'Simulator'>) => signIn('/demo', { role }),

    /** Обновление по refresh-cookie; параллельные вызовы ждут один запрос. */
    refresh(): Promise<boolean> {
      refreshing ??= (async () => {
        try {
          const response = await post('/refresh')
          if (!response.ok) return false
          accept((await response.json()) as LoginResponse)
          return true
        } catch {
          return false
        }
      })().finally(() => {
        refreshing = null
      })
      return refreshing
    },

    /** Старт приложения: есть ли живая сессия в cookie. */
    async restore(): Promise<void> {
      if (state.status !== 'unknown') return
      if (!(await this.refresh())) set({ status: 'signedOut', user: null, reason: null })
    },

    async logout(): Promise<void> {
      try {
        await post('/logout')
      } finally {
        this.clear('logout')
      }
    },

    clear(reason: SignOutReason) {
      token = null
      tokenExpiresAt = 0
      set({ status: 'signedOut', user: null, reason })
    },
  }
}

export const session = createSession()

/** «Анна К. · шеф-повар»; роль не повторяется, если имя и есть роль (демо-учётки). */
export const userLine = (user: SessionUser, role: string) =>
  user.displayName.toLocaleLowerCase() === role.toLocaleLowerCase() ? user.displayName : `${user.displayName} · ${role}`

export function useSession(): State {
  return useSyncExternalStore(session.subscribe, session.snapshot, session.snapshot)
}
