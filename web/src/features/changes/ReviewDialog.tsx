import { useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Textarea } from '@/components/ui/textarea'
import { useCrAction } from '@/api/changeRequests'
import { toastError } from '@/lib/errors'

export function ReviewDialog({
  id,
  action,
  open,
  onOpenChange,
}: {
  id: string
  action: 'approve' | 'reject'
  open: boolean
  onOpenChange: (o: boolean) => void
}) {
  const [comment, setComment] = useState('')
  const mutation = useCrAction(id, action)
  const approve = action === 'approve'
  const submit = async () => {
    try {
      await mutation.mutateAsync(comment.trim() || undefined)
      toast.success(approve ? 'Approval recorded' : 'Change request rejected')
      setComment('')
      onOpenChange(false)
    } catch (e) {
      toastError(e, approve ? 'Could not approve' : 'Could not reject')
    }
  }
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{approve ? 'Approve change request' : 'Reject change request'}</DialogTitle>
          <DialogDescription>
            {approve ? 'When enough approvals are collected the change is published immediately.' : 'The author will need to open a new change request.'}
          </DialogDescription>
        </DialogHeader>
        <Textarea placeholder="Comment (optional)" rows={3} value={comment} onChange={(e) => setComment(e.target.value)} aria-label="Review comment" />
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button variant={approve ? 'default' : 'destructive'} onClick={submit} disabled={mutation.isPending}>
            {approve ? 'Approve' : 'Reject'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
