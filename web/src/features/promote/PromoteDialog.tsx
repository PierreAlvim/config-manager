import { useMemo, useState } from 'react'
import { ArrowUpRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Field, SimpleSelect } from '@/components/common/misc'
import { usePromote } from '@/api/flags'
import type { Environment, FlagSummary, Policy } from '@/api/types'
import { toastError } from '@/lib/errors'
import { useOutcomeToast } from '@/lib/outcome'

export function PromoteDialog({
  ns,
  toEnv,
  envs,
  flags,
  policy,
}: {
  ns: string
  toEnv: string
  envs: Environment[]
  flags: FlagSummary[]
  policy?: Policy
}) {
  const [open, setOpen] = useState(false)
  const sources = envs.filter((e) => e.key !== toEnv)
  const [from, setFrom] = useState(sources[0]?.key ?? '')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [title, setTitle] = useState('')
  const promote = usePromote(ns, toEnv)
  const toastOutcome = useOutcomeToast()
  const available = useMemo(() => flags.filter((f) => f.environments[from]), [flags, from])
  const allSelected = available.length > 0 && available.every((f) => selected.has(f.key))
  const toggle = (k: string, on: boolean) =>
    setSelected((s) => {
      const n = new Set(s)
      if (on) n.add(k)
      else n.delete(k)
      return n
    })

  const go = async () => {
    try {
      const outcome = await promote.mutateAsync({
        fromEnvironment: from,
        flagKeys: [...selected].filter((k) => available.some((f) => f.key === k)),
        title: title.trim() || `Promote ${from} → ${toEnv}`,
      })
      toastOutcome(outcome)
      setOpen(false)
      setSelected(new Set())
      setTitle('')
    } catch (e) {
      toastError(e, 'Could not promote')
    }
  }

  return (
    <>
      <Button variant="outline" onClick={() => setOpen(true)} disabled={sources.length === 0}>
        <ArrowUpRight /> Promote to {toEnv}
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Promote flags to {toEnv}</DialogTitle>
            <DialogDescription>
              Copies flag configurations from another environment.{' '}
              {policy?.requireChangeRequest ? `${toEnv} requires a change request.` : `${toEnv} publishes directly.`}
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <Field label="From environment">
              <SimpleSelect
                aria-label="From environment"
                value={from}
                onChange={(v) => {
                  setFrom(v)
                  setSelected(new Set())
                }}
                options={sources.map((e) => ({ value: e.key, label: e.name || e.key }))}
              />
            </Field>
            <div className="space-y-2">
              <label className="flex items-center gap-2 text-sm font-medium">
                <Checkbox
                  checked={allSelected}
                  onCheckedChange={(c) => setSelected(c ? new Set(available.map((f) => f.key)) : new Set())}
                  aria-label="Select all flags"
                />
                Select all ({available.length})
              </label>
              <div className="max-h-56 space-y-1 overflow-auto rounded-lg border p-2">
                {available.length === 0 && <p className="p-2 text-sm text-muted-foreground">No flags are configured in {from}.</p>}
                {available.map((f) => (
                  <label key={f.key} className="flex items-center gap-2 rounded px-2 py-1 text-sm hover:bg-muted">
                    <Checkbox checked={selected.has(f.key)} onCheckedChange={(c) => toggle(f.key, c === true)} aria-label={f.key} />
                    <span className="font-mono">{f.key}</span>
                    {!f.environments[toEnv] && <span className="text-xs text-muted-foreground">(new in {toEnv})</span>}
                  </label>
                ))}
              </div>
            </div>
            <Field label="Title" htmlFor="promote-title">
              <Input id="promote-title" value={title} placeholder={`Promote ${from} → ${toEnv}`} onChange={(e) => setTitle(e.target.value)} />
            </Field>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button onClick={go} disabled={selected.size === 0 || promote.isPending}>
              Promote {selected.size} flag(s)
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
