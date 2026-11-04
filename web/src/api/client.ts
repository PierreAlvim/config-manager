import { config } from '@/config'
import type { ProblemDetails } from './types'

export class ApiError extends Error {
  status: number
  problem: ProblemDetails | null
  messages: string[]
  constructor(status: number, problem: ProblemDetails | null, fallback: string) {
    super(problem?.title || problem?.detail || fallback)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.messages = collectProblemMessages(problem)
  }
}

/** `errors` may be a string list, or the ASP.NET `{ field: [msgs] }` dictionary. */
export function collectProblemMessages(problem: ProblemDetails | null): string[] {
  if (!problem) return []
  const out: string[] = []
  const e = problem.errors
  if (Array.isArray(e)) {
    for (const x of e) out.push(typeof x === 'string' ? x : JSON.stringify(x))
  } else if (e && typeof e === 'object') {
    for (const [field, v] of Object.entries(e as Record<string, unknown>)) {
      const msgs = Array.isArray(v) ? v : [v]
      for (const m of msgs) out.push(field ? `${field}: ${String(m)}` : String(m))
    }
  }
  return out
}

interface AuthHandlers {
  getToken: () => string | undefined
  onUnauthorized: () => void
}
let handlers: AuthHandlers = { getToken: () => undefined, onUnauthorized: () => {} }
export function setAuthHandlers(h: AuthHandlers) {
  handlers = h
}

export interface ApiResult<T> {
  status: number
  data: T
}

export async function apiRequest<T>(
  path: string,
  init: { method?: string; body?: unknown; signal?: AbortSignal } = {},
): Promise<ApiResult<T>> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const token = handlers.getToken()
  if (token) headers.Authorization = `Bearer ${token}`
  if (init.body !== undefined) headers['Content-Type'] = 'application/json'
  const res = await fetch(`${config.apiBase}${path}`, {
    method: init.method ?? 'GET',
    headers,
    body: init.body !== undefined ? JSON.stringify(init.body) : undefined,
    signal: init.signal,
  })
  const text = await res.text()
  let parsed: unknown = null
  if (text) {
    try {
      parsed = JSON.parse(text)
    } catch {
      parsed = null
    }
  }
  if (!res.ok) {
    if (res.status === 401) handlers.onUnauthorized()
    const problem = parsed && typeof parsed === 'object' ? (parsed as ProblemDetails) : null
    throw new ApiError(res.status, problem, `Request failed (${res.status})`)
  }
  return { status: res.status, data: parsed as T }
}

export async function api<T>(path: string, init?: Parameters<typeof apiRequest>[1]): Promise<T> {
  return (await apiRequest<T>(path, init)).data
}

export function qs(params: Record<string, string | number | undefined | null>): string {
  const sp = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') sp.set(k, String(v))
  const s = sp.toString()
  return s ? `?${s}` : ''
}
