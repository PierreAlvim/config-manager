import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, qs } from './client'
import type { AuditEntry, Member, Policy, Role, ServiceAccess } from './types'

export const usePolicies = (ns: string) =>
  useQuery({ queryKey: ['policies', ns], queryFn: () => api<Policy[]>(`/api/namespaces/${ns}/policies`) })
export function useUpdatePolicy(ns: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (p: Policy) =>
      api(`/api/namespaces/${ns}/policies/${p.environment}`, {
        method: 'PUT',
        body: { requireChangeRequest: p.requireChangeRequest, requiredApprovals: p.requiredApprovals },
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['policies', ns] }),
  })
}

export const useMembers = (ns: string, enabled = true) =>
  useQuery({ queryKey: ['members', ns], queryFn: () => api<Member[]>(`/api/namespaces/${ns}/members`), enabled })
export function useAddMember(ns: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (m: { subjectType: 'user' | 'group'; subject: string; role: Role }) =>
      api(`/api/namespaces/${ns}/members`, { method: 'POST', body: m }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['members', ns] }),
  })
}
export function useDeleteMember(ns: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api(`/api/namespaces/${ns}/members/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['members', ns] }),
  })
}

export const useServiceAccess = (ns: string, enabled = true) =>
  useQuery({
    queryKey: ['service-access', ns],
    queryFn: () => api<ServiceAccess[]>(`/api/namespaces/${ns}/service-access`),
    enabled,
  })
export function useAddServiceAccess(ns: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (b: { clientId: string; environment: string }) =>
      api(`/api/namespaces/${ns}/service-access`, { method: 'POST', body: b }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['service-access', ns] }),
  })
}
export function useDeleteServiceAccess(ns: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api(`/api/namespaces/${ns}/service-access/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['service-access', ns] }),
  })
}

export const useAudit = (namespace?: string, take = 100) =>
  useQuery({
    queryKey: ['audit', namespace ?? '', take],
    queryFn: () => api<AuditEntry[]>(`/api/audit${qs({ namespace, take })}`),
  })
