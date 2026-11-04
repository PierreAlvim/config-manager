import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api, apiRequest, setAuthHandlers } from './client'

function mockFetch(status: number, body: unknown, contentType = 'application/json') {
  const fn = vi.fn().mockResolvedValue(
    new Response(body === undefined ? null : typeof body === 'string' ? body : JSON.stringify(body), {
      status,
      headers: { 'Content-Type': contentType },
    }),
  )
  vi.stubGlobal('fetch', fn)
  return fn
}

describe('api client', () => {
  const onUnauthorized = vi.fn()
  beforeEach(() => {
    onUnauthorized.mockReset()
    setAuthHandlers({ getToken: () => 'tok123', onUnauthorized })
  })

  it('sends the Bearer token and JSON body', async () => {
    const f = mockFetch(200, { ok: true })
    const res = await api<{ ok: boolean }>('/api/x', { method: 'POST', body: { a: 1 } })
    expect(res).toEqual({ ok: true })
    const [url, init] = f.mock.calls[0]
    expect(url).toBe('/api/x')
    expect(init.method).toBe('POST')
    expect(init.headers.Authorization).toBe('Bearer tok123')
    expect(init.headers['Content-Type']).toBe('application/json')
    expect(init.body).toBe('{"a":1}')
  })

  it('exposes the HTTP status (200 vs 202)', async () => {
    mockFetch(202, { outcome: 'changeRequestCreated' })
    const r = await apiRequest('/api/x', { method: 'POST', body: {} })
    expect(r.status).toBe(202)
  })

  it('parses ProblemDetails with an errors list', async () => {
    mockFetch(400, { title: 'Validation failed', detail: 'bad', errors: ['Rule #1: nope', 'Other'] })
    const err = (await api('/api/x').catch((e) => e)) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.status).toBe(400)
    expect(err.message).toBe('Validation failed')
    expect(err.messages).toEqual(['Rule #1: nope', 'Other'])
  })

  it('parses ASP.NET errors dictionaries', async () => {
    mockFetch(400, { title: 'One or more validation errors occurred.', errors: { Key: ['Required'] } })
    const err = (await api('/api/x').catch((e) => e)) as ApiError
    expect(err.messages).toEqual(['Key: Required'])
  })

  it('falls back to a generic message for non-JSON errors', async () => {
    mockFetch(500, 'boom', 'text/plain')
    const err = (await api('/api/x').catch((e) => e)) as ApiError
    expect(err.message).toBe('Request failed (500)')
  })

  it('triggers sign-in on 401', async () => {
    mockFetch(401, { title: 'Unauthorized' })
    await expect(api('/api/x')).rejects.toBeInstanceOf(ApiError)
    expect(onUnauthorized).toHaveBeenCalledOnce()
  })

  it('does not trigger sign-in on 403', async () => {
    mockFetch(403, { title: 'Forbidden' })
    await expect(api('/api/x')).rejects.toMatchObject({ status: 403 })
    expect(onUnauthorized).not.toHaveBeenCalled()
  })
})
