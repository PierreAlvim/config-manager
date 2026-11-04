import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Plus } from 'lucide-react'
import { toast } from 'sonner'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Field, SimpleSelect } from '@/components/common/misc'
import { useCreateFlag } from '@/api/flags'
import { FLAG_TYPES, type FlagType } from '@/api/types'
import { isValidKey } from '@/lib/flagValidator'
import { toastError } from '@/lib/errors'

const schema = z.object({
  key: z.string().refine(isValidKey, 'Lowercase letters, digits, "-", "_", "." (max 64)'),
  name: z.string().min(1, 'Required'),
  description: z.string(),
  type: z.enum(FLAG_TYPES as [FlagType, ...FlagType[]]),
})
type Values = z.infer<typeof schema>

export function NewFlagDialog({ ns, env }: { ns: string; env: string }) {
  const [open, setOpen] = useState(false)
  const navigate = useNavigate()
  const create = useCreateFlag(ns)
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { key: '', name: '', description: '', type: 'boolean' },
  })
  const type = useWatch({ control: form.control, name: 'type' })
  const errs = form.formState.errors
  const submit = form.handleSubmit(async (v) => {
    try {
      await create.mutateAsync(v)
      toast.success(`Flag ${v.key} created. Configure it in an environment to publish it.`)
      form.reset()
      setOpen(false)
      navigate(`/namespaces/${ns}/flags/${v.key}?env=${env}`)
    } catch (e) {
      toastError(e, 'Could not create flag')
    }
  })
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={<Button />}>
        <Plus /> New flag
      </DialogTrigger>
      <DialogContent>
        <form onSubmit={submit} className="space-y-4">
          <DialogHeader>
            <DialogTitle>New flag</DialogTitle>
            <DialogDescription>The type cannot be changed later.</DialogDescription>
          </DialogHeader>
          <Field label="Key" htmlFor="flag-key" hint={errs.key?.message ?? 'e.g. new-checkout'}>
            <Input id="flag-key" autoComplete="off" aria-invalid={!!errs.key} {...form.register('key')} />
          </Field>
          <Field label="Name" htmlFor="flag-name" hint={errs.name?.message}>
            <Input id="flag-name" aria-invalid={!!errs.name} {...form.register('name')} />
          </Field>
          <Field label="Description" htmlFor="flag-desc">
            <Textarea id="flag-desc" rows={2} {...form.register('description')} />
          </Field>
          <Field label="Type">
            <SimpleSelect
              aria-label="Flag type"
              value={type}
              onChange={(t) => form.setValue('type', t as FlagType)}
              options={FLAG_TYPES.map((t) => ({ value: t }))}
            />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={create.isPending}>
              Create flag
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
