import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, qs } from './client'
import type { ChangeRequestDetail, ChangeRequestSummary } from './types'

export interface CrFilter {
  namespace?: string
  environment?: string
  status?: string
}
export const useChangeRequests = (f: CrFilter = {}) =>
  useQuery({
    queryKey: ['change-requests', f],
    queryFn: () => api<ChangeRequestSummary[]>(`/api/change-requests${qs({ ...f })}`),
  })
export const usePendingCount = () =>
  useQuery({
    queryKey: ['change-requests', 'pending-count'],
    queryFn: async () => (await api<ChangeRequestSummary[]>(`/api/change-requests${qs({ status: 'pending' })}`)).length,
    refetchInterval: 60_000,
  })
export const useChangeRequest = (id: string) =>
  useQuery({ queryKey: ['change-request', id], queryFn: () => api<ChangeRequestDetail>(`/api/change-requests/${id}`) })

export function useCrAction(id: string, action: 'approve' | 'reject' | 'cancel') {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (comment?: string) =>
      api(`/api/change-requests/${id}/${action}`, { method: 'POST', body: action === 'cancel' ? undefined : { comment } }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['change-request', id] })
      void qc.invalidateQueries({ queryKey: ['change-requests'] })
      void qc.invalidateQueries({ queryKey: ['flags'] })
      void qc.invalidateQueries({ queryKey: ['config'] })
      void qc.invalidateQueries({ queryKey: ['versions'] })
    },
  })
}
