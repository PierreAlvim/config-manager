import { useQuery } from '@tanstack/react-query'
import { api } from './client'
import type { Environment, Me, Role } from './types'
import { ROLES } from './types'

export const useMe = () => useQuery({ queryKey: ['me'], queryFn: () => api<Me>('/api/me'), staleTime: 60_000 })
export const useEnvironments = () =>
  useQuery({
    queryKey: ['environments'],
    queryFn: async () => (await api<Environment[]>('/api/environments')).sort((a, b) => a.sortOrder - b.sortOrder),
    staleTime: 5 * 60_000,
  })

export function roleAtLeast(role: Role | null | undefined, min: Role): boolean {
  if (!role) return false
  return ROLES.indexOf(role) >= ROLES.indexOf(min)
}

/** Effective role of the current user in a namespace (admin = owner everywhere). */
export function effectiveRole(me: Me | undefined, ns: string, myRole?: Role | null): Role | null {
  if (me?.isAdmin) return 'owner'
  return myRole ?? me?.namespaceRoles?.[ns] ?? null
}
