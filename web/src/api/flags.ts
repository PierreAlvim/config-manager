import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiRequest } from './client'
import type {
  ChangeOutcome,
  ConfigSnapshot,
  FlagDetail,
  FlagSummary,
  FlagType,
  SubmitChangeBody,
} from './types'

export const useFlags = (ns: string) =>
  useQuery({ queryKey: ['flags', ns], queryFn: () => api<FlagSummary[]>(`/api/namespaces/${ns}/flags`) })
export const useFlag = (ns: string, key: string) =>
  useQuery({ queryKey: ['flags', ns, key], queryFn: () => api<FlagDetail>(`/api/namespaces/${ns}/flags/${key}`) })
export const useEnvConfig = (ns: string, env: string) =>
  useQuery({
    queryKey: ['config', ns, env],
    queryFn: () => api<ConfigSnapshot>(`/api/namespaces/${ns}/environments/${env}/config`),
    enabled: !!env,
  })

export function useCreateFlag(ns: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (b: { key: string; name: string; description: string; type: FlagType }) =>
      api<FlagDetail>(`/api/namespaces/${ns}/flags`, { method: 'POST', body: b }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['flags', ns] }),
  })
}
export function useUpdateFlag(ns: string, key: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (b: { name: string; description: string }) =>
      api(`/api/namespaces/${ns}/flags/${key}`, { method: 'PUT', body: b }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['flags', ns] }),
  })
}
export function useDeleteFlag(ns: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (key: string) => api(`/api/namespaces/${ns}/flags/${key}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['flags', ns] }),
  })
}

/** Normalizes 200/202 into a ChangeOutcome. */
export async function postChange(path: string, body: unknown): Promise<ChangeOutcome> {
  const { status, data } = await apiRequest<ChangeOutcome>(path, { method: 'POST', body })
  if (data && 'outcome' in data) return data
  return status === 202 ? (data as ChangeOutcome) : { outcome: 'applied', version: (data as { version: number }).version }
}

export function useInvalidateEnv(ns: string, env?: string) {
  const qc = useQueryClient()
  return () => {
    void qc.invalidateQueries({ queryKey: ['flags', ns] })
    void qc.invalidateQueries({ queryKey: ['config', ns] })
    void qc.invalidateQueries({ queryKey: ['versions', ns, env] })
    void qc.invalidateQueries({ queryKey: ['change-requests'] })
    void qc.invalidateQueries({ queryKey: ['audit'] })
  }
}

export function useSubmitChange(ns: string, env: string) {
  const invalidate = useInvalidateEnv(ns, env)
  return useMutation({
    mutationFn: (b: SubmitChangeBody) => postChange(`/api/namespaces/${ns}/environments/${env}/changes`, b),
    onSuccess: invalidate,
  })
}
export function usePromote(ns: string, env: string) {
  const invalidate = useInvalidateEnv(ns, env)
  return useMutation({
    mutationFn: (b: { fromEnvironment: string; flagKeys?: string[]; title?: string }) =>
      postChange(`/api/namespaces/${ns}/environments/${env}/promote`, b),
    onSuccess: invalidate,
  })
}
