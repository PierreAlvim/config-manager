import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from './client'
import { postChange, useInvalidateEnv } from './flags'
import type { VersionDetail, VersionInfo } from './types'

export const useVersions = (ns: string, env: string) =>
  useQuery({
    queryKey: ['versions', ns, env],
    queryFn: () => api<VersionInfo[]>(`/api/namespaces/${ns}/environments/${env}/versions`),
    enabled: !!env,
  })
export const useVersion = (ns: string, env: string, v: number | null) =>
  useQuery({
    queryKey: ['versions', ns, env, v],
    queryFn: () => api<VersionDetail>(`/api/namespaces/${ns}/environments/${env}/versions/${v}`),
    enabled: !!env && v !== null,
  })
export function useRollback(ns: string, env: string) {
  const invalidate = useInvalidateEnv(ns, env)
  return useMutation({
    mutationFn: ({ version, reason }: { version: number; reason?: string }) =>
      postChange(`/api/namespaces/${ns}/environments/${env}/versions/${version}/rollback`, { reason }),
    onSuccess: invalidate,
  })
}
