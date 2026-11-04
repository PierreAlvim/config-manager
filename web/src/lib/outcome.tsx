import { useNavigate } from 'react-router-dom'
import { toast } from 'sonner'
import type { ChangeOutcome } from '@/api/types'

/** Shows the right toast for a 200 (applied) or 202 (change request created) response. */
export function useOutcomeToast() {
  const navigate = useNavigate()
  return (o: ChangeOutcome) => {
    if (o.outcome === 'applied') {
      toast.success(`Published v${o.version}`)
    } else {
      const id = o.changeRequest.id
      toast.info('Change request created', {
        description: 'This environment requires review before the change is published.',
        action: { label: 'View', onClick: () => navigate(`/change-requests/${id}`) },
        duration: 10000,
      })
    }
  }
}
