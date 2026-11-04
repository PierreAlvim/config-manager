import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Field } from '@/components/common/misc'
import { useSubmitChange } from '@/api/flags'
import type { ChangeOutcome, Operation, Policy } from '@/api/types'
import { toastError } from '@/lib/errors'
import { useOutcomeToast } from '@/lib/outcome'

export function SubmitChangeDialog({
  ns,
  env,
  operations,
  defaultTitle,
  policy,
  open,
  onOpenChange,
  onDone,
  confirmLabel = 'Submit change',
}: {
  ns: string
  env: string
  operations: Operation[]
  defaultTitle: string
  policy?: Policy
  open: boolean
  onOpenChange: (o: boolean) => void
  onDone?: (o: ChangeOutcome) => void
  confirmLabel?: string
}) {
  const [title, setTitle] = useState(defaultTitle)
  const [description, setDescription] = useState('')
  const [force, setForce] = useState(false)
  const submit = useSubmitChange(ns, env)
  const toastOutcome = useOutcomeToast()
  const required = !!policy?.requireChangeRequest
  const willReview = required || force

  const go = async () => {
    try {
      const outcome = await submit.mutateAsync({
        title: title.trim(),
        description: description.trim() || undefined,
        forceChangeRequest: force || undefined,
        operations,
      })
      toastOutcome(outcome)
      onOpenChange(false)
      onDone?.(outcome)
    } catch (e) {
      toastError(e, 'Could not submit change')
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Submit change to {env}</DialogTitle>
          <DialogDescription>
            {willReview
              ? `This will open a change request${required ? ` (needs ${policy?.requiredApprovals ?? 1} approval(s))` : ''}.`
              : 'This environment allows direct publishing: the change goes live immediately.'}
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <Field label="Title" htmlFor="change-title">
            <Input id="change-title" value={title} onChange={(e) => setTitle(e.target.value)} autoFocus />
          </Field>
          <Field label="Description (optional)" htmlFor="change-desc">
            <Textarea id="change-desc" rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
          </Field>
          {!required && (
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={force} onCheckedChange={(c) => setForce(c === true)} aria-label="Request review even if not required" />
              Request review even if not required
            </label>
          )}
          <p className="text-xs text-muted-foreground">
            {operations.length} operation(s): {operations.map((o) => `${o.type} ${o.flagKey}`).join(', ')}
          </p>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button onClick={go} disabled={!title.trim() || submit.isPending}>
            {willReview ? 'Open change request' : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
