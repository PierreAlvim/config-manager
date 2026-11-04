import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Plus } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Field } from '@/components/common/misc'
import { useCreateNamespace } from '@/api/namespaces'
import { isValidKey } from '@/lib/flagValidator'
import { toastError } from '@/lib/errors'

const schema = z.object({
  key: z.string().refine(isValidKey, 'Lowercase letters, digits, "-", "_", "." (max 64)'),
  name: z.string().min(1, 'Required'),
  description: z.string(),
})
type Values = z.infer<typeof schema>

export function CreateNamespaceDialog() {
  const [open, setOpen] = useState(false)
  const create = useCreateNamespace()
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { key: '', name: '', description: '' } })
  const submit = form.handleSubmit(async (v) => {
    try {
      await create.mutateAsync(v)
      toast.success(`Namespace ${v.key} created`)
      form.reset()
      setOpen(false)
    } catch (e) {
      toastError(e, 'Could not create namespace')
    }
  })
  const errs = form.formState.errors
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={<Button />}>
        <Plus /> New namespace
      </DialogTrigger>
      <DialogContent>
        <form onSubmit={submit} className="space-y-4">
          <DialogHeader>
            <DialogTitle>Create namespace</DialogTitle>
            <DialogDescription>A namespace isolates the flags of one team or service domain.</DialogDescription>
          </DialogHeader>
          <Field label="Key" htmlFor="ns-key" hint={errs.key?.message ?? 'Used in URLs and by SDKs, e.g. orders'}>
            <Input id="ns-key" {...form.register('key')} aria-invalid={!!errs.key} autoComplete="off" />
          </Field>
          <Field label="Name" htmlFor="ns-name" hint={errs.name?.message}>
            <Input id="ns-name" {...form.register('name')} aria-invalid={!!errs.name} />
          </Field>
          <Field label="Description" htmlFor="ns-desc">
            <Textarea id="ns-desc" rows={3} {...form.register('description')} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={create.isPending}>
              Create
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
