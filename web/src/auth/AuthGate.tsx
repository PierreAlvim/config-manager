import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { AuthProvider, useAuth } from 'react-oidc-context'
import { WebStorageStateStore, type User } from 'oidc-client-ts'
import { Loader2, ShieldCheck, Flag } from 'lucide-react'
import { config } from '@/config'
import { setAuthHandlers } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'

export const oidcConfig = {
  authority: config.oidcAuthority,
  client_id: config.oidcClientId,
  redirect_uri: `${window.location.origin}/callback`,
  post_logout_redirect_uri: window.location.origin,
  scope: 'openid profile email',
  response_type: 'code',
  userStore: new WebStorageStateStore({ store: window.localStorage }),
  automaticSilentRenew: true,
  onSigninCallback: (user: User | void) => {
    const state = (user as User | undefined)?.state as { returnTo?: string } | undefined
    // drop ?code=&state= from the URL; the router mounts afterwards so it sees the final path
    window.history.replaceState({}, document.title, state?.returnTo || '/')
  },
}

function currentPath() {
  const p = window.location.pathname + window.location.search
  return p.startsWith('/callback') ? '/' : p
}

export const UNAUTHORIZED_MESSAGE = 'Your session was rejected by the API. Please sign in again.'

function Bridge({ children }: { children: ReactNode }) {
  const auth = useAuth()
  const [sessionError, setSessionError] = useState<string>()

  const recovering = useRef(false)

  // useLayoutEffect, not useEffect: children fire their queries in (passive) effects, and those
  // run child-first — before a parent's useEffect. Layout effects all run earlier, in the commit
  // phase, so the token is installed before the first request (otherwise it goes out anonymous -> 401).
  useLayoutEffect(() => {
    setAuthHandlers({
      getToken: () => auth.user?.access_token,
      // Never auto-redirect on 401: with an active IdP session the redirect bounces straight
      // back and any persistent 401 becomes an infinite loop. Try a silent renew (refresh
      // token) once; if that fails, drop the session and show the landing page with an error.
      onUnauthorized: () => {
        if (recovering.current) return
        recovering.current = true
        void (async () => {
          try {
            if (!auth.user) throw new Error('no session')
            await auth.signinSilent()
          } catch {
            setSessionError(UNAUTHORIZED_MESSAGE)
            await auth.removeUser()
          } finally {
            recovering.current = false
          }
        })()
      },
    })
  }, [auth])

  if (auth.isLoading) {
    return (
      <div className="flex min-h-svh items-center justify-center text-muted-foreground">
        <Loader2 className="mr-2 size-5 animate-spin" /> Signing you in…
      </div>
    )
  }
  if (!auth.isAuthenticated) return <Landing error={sessionError ?? auth.error?.message} />
  return <>{children}</>
}

function Landing({ error }: { error?: string }) {
  const auth = useAuth()
  return (
    <div className="flex min-h-svh items-center justify-center bg-gradient-to-br from-background to-muted p-4">
      <Card className="w-full max-w-md">
        <CardHeader className="items-center text-center">
          <div className="mb-2 flex size-12 items-center justify-center rounded-xl bg-primary text-primary-foreground">
            <Flag className="size-6" />
          </div>
          <CardTitle className="text-xl">Config Manager</CardTitle>
          <CardDescription>
            Feature flags and runtime configuration, with environments, reviews and rollbacks.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {error && (
            <p role="alert" className="mb-3 rounded-md bg-destructive/10 p-2 text-sm text-destructive">
              {error}
            </p>
          )}
          <Button
            className="w-full"
            size="lg"
            onClick={() => void auth.signinRedirect({ state: { returnTo: currentPath() } })}
          >
            <ShieldCheck /> Sign in with SSO
          </Button>
        </CardContent>
        <CardFooter className="justify-center text-xs text-muted-foreground">
          You will be redirected to your identity provider.
        </CardFooter>
      </Card>
    </div>
  )
}

export function AuthGate({ children }: { children: ReactNode }) {
  return (
    <AuthProvider {...oidcConfig}>
      <Bridge>{children}</Bridge>
    </AuthProvider>
  )
}
