import { render, screen, waitFor } from '@testing-library/react'
import { useEffect } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '@/api/client'

// Mock react-oidc-context: an authenticated user whose token we can inspect.
const auth = {
  isLoading: false,
  isAuthenticated: true,
  user: { access_token: 'tok-123' } as { access_token: string } | null,
  error: undefined,
  signinRedirect: vi.fn(),
  signinSilent: vi.fn(),
  removeUser: vi.fn(),
}
vi.mock('react-oidc-context', () => ({
  AuthProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  useAuth: () => auth,
}))

const { AuthGate, UNAUTHORIZED_MESSAGE } = await import('./AuthGate')

/** Fires a request in its own effect — child effects run BEFORE the parent's effects. */
function ChildThatFetches() {
  useEffect(() => {
    void api('/api/me').catch(() => {})
  }, [])
  return <p>app</p>
}

describe('AuthGate', () => {
  const fetchMock = vi.fn()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    auth.isAuthenticated = true
    auth.user = { access_token: 'tok-123' }
    auth.signinRedirect.mockReset()
    auth.signinSilent.mockReset()
    auth.removeUser.mockReset().mockImplementation(async () => {
      auth.isAuthenticated = false
      auth.user = null
    })
  })
  afterEach(() => vi.unstubAllGlobals())

  it('sends the bearer token on requests fired from child effects during the first render', async () => {
    fetchMock.mockResolvedValue(new Response('{}', { status: 200 }))
    render(<AuthGate><ChildThatFetches /></AuthGate>)

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    const headers = fetchMock.mock.calls[0][1].headers as Record<string, string>
    expect(headers.Authorization).toBe('Bearer tok-123')
  })

  it('never auto-redirects on 401 (prevents the SSO login loop); tries silent renew, then shows the landing', async () => {
    fetchMock.mockResolvedValue(new Response('', { status: 401 }))
    auth.signinSilent.mockRejectedValue(new Error('refresh failed'))
    const { rerender } = render(<AuthGate><ChildThatFetches /></AuthGate>)

    await waitFor(() => expect(auth.removeUser).toHaveBeenCalledTimes(1))
    expect(auth.signinSilent).toHaveBeenCalledTimes(1)
    expect(auth.signinRedirect).not.toHaveBeenCalled()

    rerender(<AuthGate><ChildThatFetches /></AuthGate>)
    expect(await screen.findByText(UNAUTHORIZED_MESSAGE)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /sign in with sso/i })).toBeInTheDocument()
  })
})
