/** RFC 7807 ProblemDetails, как его отдаёт API (+ расширения: code, missing, errors…). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  traceId?: string
  code?: string
  errors?: Record<string, string[]>
  [extension: string]: unknown
}

/**
 * Ошибка API. Текст для человека интерфейс берёт по `code` из переводов (errors.<code>),
 * `message` — запасной вариант (текст сервера).
 */
export class ApiError extends Error {
  readonly status: number
  readonly problem?: ProblemDetails

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  get code(): string | undefined {
    return this.problem?.code ?? (this.status === 0 ? 'network' : undefined)
  }
}
