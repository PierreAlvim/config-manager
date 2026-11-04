import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { TooltipProvider } from '@/components/ui/tooltip'
import { setAuthHandlers } from '@/api/client'
import NamespacePage from './NamespacePage'
import { FlagsTable } from '@/features/flags/FlagsTable'
import type { FlagSummary } from '@/api/types'

const flags: FlagSummary[] = [
  {
    key: 'new-checkout',
    name: 'New checkout',
    type: 'boolean',
    environments: { dev: { enabled: true, defaultVariant: 'off', rulesCount: 2 }, prod: null },
  },
  {
    key: 'max-items',
    name: 'Max items',
    type: 'integer',
    environments: { dev: { enabled: false, defaultVariant: 'small', rulesCount: 0 }, prod: null },
  },
]

const routes: Record<string, unknown> = {
  '/api/me': { username: 'alice', isAdmin: false, groups: [], namespaceRoles: { orders: 'editor' } },
  '/api/environments': [
    { key: 'dev', name: 'dev', sortOrder: 1 },
    { key: 'prod', name: 'prod', sortOrder: 3 },
  ],
  '/api/namespaces/orders': { key: 'orders', name: 'Orders', description: 'Orders domain', myRole: 'editor' },
  '/api/namespaces': [{ key: 'orders', name: 'Orders', myRole: 'editor' }],
  '/api/namespaces/orders/flags': flags,
  '/api/namespaces/orders/policies': [
    { environment: 'dev', requireChangeRequest: false, requiredApprovals: 1 },
    { environment: 'prod', requireChangeRequest: true, requiredApprovals: 2 },
  ],
  '/api/namespaces/orders/environments/dev/config': { namespace: 'orders', environment: 'dev', version: 7, publishedAt: '2026-01-01T00:00:00Z', flags: {} },
  '/api/namespaces/orders/environments/prod/config': { namespace: 'orders', environment: 'prod', version: 3, publishedAt: '2026-01-01T00:00:00Z', flags: {} },
}

function renderPage(path: string) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <TooltipProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/namespaces/:ns" element={<NamespacePage />} />
          </Routes>
        </MemoryRouter>
      </TooltipProvider>
    </QueryClientProvider>,
  )
}

describe('FlagsTable', () => {
  it('renders rows with type, enabled switch, default variant and rule count', () => {
    render(
      <MemoryRouter>
        <FlagsTable ns="orders" env="dev" flags={flags} canEdit={false} onToggle={() => {}} />
      </MemoryRouter>,
    )
    const rows = screen.getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(within(rows[0]).getByText('new-checkout')).toBeInTheDocument()
    expect(within(rows[0]).getByText('boolean')).toBeInTheDocument()
    expect(within(rows[0]).getByRole('switch')).toBeChecked()
    expect(within(rows[0]).getByText('off')).toBeInTheDocument()
    expect(within(rows[1]).getByRole('switch')).not.toBeChecked()
  })

  it('shows "not configured" for environments without config', () => {
    render(
      <MemoryRouter>
        <FlagsTable ns="orders" env="prod" flags={flags} canEdit onToggle={() => {}} />
      </MemoryRouter>,
    )
    expect(screen.getAllByText('not configured')).toHaveLength(2)
    expect(screen.queryByRole('switch')).toBeNull()
  })
})

describe('NamespacePage', () => {
  beforeEach(() => {
    setAuthHandlers({ getToken: () => 't', onUnauthorized: () => {} })
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        const body = routes[url.split('?')[0]]
        return body === undefined
          ? new Response(JSON.stringify({ title: 'Not found' }), { status: 404 })
          : new Response(JSON.stringify(body), { status: 200 })
      }),
    )
  })
  afterEach(() => vi.unstubAllGlobals())

  it('lists flags, version and policy badge for the selected environment', async () => {
    renderPage('/namespaces/orders?env=dev')
    expect(await screen.findByText('new-checkout')).toBeInTheDocument()
    expect(screen.getByText('max-items')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByTestId('env-version')).toHaveTextContent('v7'))
    expect(screen.getByTestId('env-policy')).toHaveTextContent('Direct publish')
    expect(screen.getByRole('button', { name: /New flag/ })).toBeInTheDocument()
  })

  it('shows change-request requirement for prod', async () => {
    renderPage('/namespaces/orders?env=prod')
    await waitFor(() => expect(screen.getByTestId('env-policy')).toHaveTextContent('Change request required · 2 approvals'))
    await waitFor(() => expect(screen.getByTestId('env-version')).toHaveTextContent('v3'))
  })

  it('hides write actions for viewers', async () => {
    routes['/api/namespaces/orders'] = { key: 'orders', name: 'Orders', myRole: 'viewer' }
    routes['/api/me'] = { username: 'dave', isAdmin: false, groups: [], namespaceRoles: { orders: 'viewer' } }
    try {
      renderPage('/namespaces/orders?env=dev')
      await screen.findByText('new-checkout')
      expect(screen.queryByRole('button', { name: /New flag/ })).toBeNull()
      expect(screen.queryByRole('link', { name: /Settings/ })).toBeNull()
    } finally {
      routes['/api/namespaces/orders'] = { key: 'orders', name: 'Orders', description: 'Orders domain', myRole: 'editor' }
      routes['/api/me'] = { username: 'alice', isAdmin: false, groups: [], namespaceRoles: { orders: 'editor' } }
    }
  })
})
