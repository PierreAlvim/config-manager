import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { Namespace } from './types'

export const useNamespaces = () => useQuery({ queryKey: ['namespaces'], queryFn: () => api<Namespace[]>('/api/namespaces') })
export const useNamespace = (ns: string) =>
  useQuery({ queryKey: ['namespaces', ns], queryFn: () => api<Namespace>(`/api/namespaces/${ns}`) })

export function useCreateNamespace() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (b: { key: string; name: string; description: string }) =>
      api<Namespace>('/api/namespaces', { method: 'POST', body: b }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['namespaces'] }),
  })
}
