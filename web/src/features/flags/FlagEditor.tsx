import { useMemo, useState } from 'react'
import { AlertTriangle, RotateCcw, Save, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Switch } from '@/components/ui/switch'
import { SimpleSelect } from '@/components/common/misc'
import { VariantsEditor } from './VariantsEditor'
import { RulesEditor } from './RulesEditor'
import { SubmitChangeDialog } from '@/features/changes/SubmitChangeDialog'
import type { FlagDefinition, FlagType, Operation, Policy } from '@/api/types'
import { flagToForm, formToFlag, removeVariantRefs, renameVariant, type FlagForm } from '@/lib/flagForm'
import { stableStringify } from '@/lib/diff'

export function FlagEditor({
  ns,
  env,
  flagKey,
  type,
  initial,
  policy,
  canEdit,
  isNew = false,
}: {
  ns: string
  env: string
  flagKey: string
  type: FlagType
  initial: FlagDefinition
  policy?: Policy
  canEdit: boolean
  isNew?: boolean
}) {
  const [form, setForm] = useState<FlagForm>(() => flagToForm(initial))
  const [dialog, setDialog] = useState<null | 'save' | 'delete'>(null)
  const built = useMemo(() => formToFlag(type, flagKey, form), [type, flagKey, form])
  const dirty = isNew || !built.flag || stableStringify(built.flag) !== stableStringify({ ...initial, metadata: initial.metadata ?? {} })
  const variantNames = form.variants.map((v) => v.name).filter((n) => n.trim())
  const disabled = !canEdit

  const ops: Operation[] =
    dialog === 'delete'
      ? [{ type: 'delete', flagKey }]
      : built.flag
        ? [{ type: 'upsert', flagKey, flag: built.flag }]
        : []

  return (
    <div className="space-y-6">
      {!canEdit && (
        <div className="rounded-lg border bg-muted/50 p-3 text-sm text-muted-foreground">
          You have read-only access to this namespace. Editing requires the editor role.
        </div>
      )}
      <Card>
        <CardHeader>
          <CardTitle>Status</CardTitle>
          <CardDescription>A disabled flag always resolves to the caller's code default.</CardDescription>
        </CardHeader>
        <CardContent>
          <label className="flex items-center gap-3 text-sm font-medium">
            <Switch
              checked={form.enabled}
              disabled={disabled}
              aria-label="Enabled"
              onCheckedChange={(c) => setForm({ ...form, enabled: c })}
            />
            {form.enabled ? 'Enabled' : 'Disabled'} in {env}
          </label>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Variants</CardTitle>
          <CardDescription>Named values this flag can resolve to ({type}).</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <VariantsEditor
            type={type}
            rows={form.variants}
            disabled={disabled}
            onChange={(variants) => setForm((f) => ({ ...f, variants }))}
            onRename={(from, to) => setForm((f) => renameVariant(f, from, to))}
            onRemove={(name) =>
              setForm((f) => removeVariantRefs({ ...f, variants: f.variants.filter((v) => v.name !== name) }, name))
            }
          />
          <div className="max-w-xs space-y-1.5">
            <label className="text-sm font-medium" id="default-variant-label">
              Default variant
            </label>
            <SimpleSelect
              aria-label="Default variant"
              value={form.defaultVariant}
              disabled={disabled}
              onChange={(v) => setForm({ ...form, defaultVariant: v })}
              options={variantNames.map((v) => ({ value: v }))}
              placeholder="Select variant"
            />
            <p className="text-xs text-muted-foreground">Served when enabled and no rule matches.</p>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Targeting rules</CardTitle>
          <CardDescription>Evaluated top to bottom; the first rule whose conditions all match wins.</CardDescription>
        </CardHeader>
        <CardContent>
          <RulesEditor
            rules={form.rules}
            variantNames={variantNames}
            disabled={disabled}
            onChange={(rules) => setForm((f) => ({ ...f, rules }))}
          />
        </CardContent>
      </Card>

      {built.errors.length > 0 && (
        <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive" data-testid="validation-errors">
          <p className="mb-1 flex items-center gap-2 font-medium">
            <AlertTriangle className="size-4" /> Fix these issues before saving
          </p>
          <ul className="list-disc space-y-0.5 pl-5">
            {built.errors.map((e, i) => (
              <li key={i}>{e}</li>
            ))}
          </ul>
        </div>
      )}

      {canEdit && (
        <div className="sticky bottom-0 -mx-4 flex flex-wrap items-center justify-between gap-2 border-t bg-background/90 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
          {isNew ? (
            <span className="text-sm text-muted-foreground">New configuration, not published yet.</span>
          ) : (
            <Button variant="destructive" onClick={() => setDialog('delete')}>
              <Trash2 /> Remove from {env}
            </Button>
          )}
          <div className="flex items-center gap-2">
            {dirty && <span className="text-xs text-muted-foreground">Unsaved changes</span>}
            <Button variant="outline" disabled={!dirty} onClick={() => setForm(flagToForm(initial))}>
              <RotateCcw /> Reset
            </Button>
            <Button disabled={!dirty || !built.flag} onClick={() => setDialog('save')}>
              <Save /> Save changes
            </Button>
          </div>
        </div>
      )}

      {dialog && (
        <SubmitChangeDialog
          ns={ns}
          env={env}
          policy={policy}
          operations={ops}
          open
          onOpenChange={(o) => !o && setDialog(null)}
          defaultTitle={dialog === 'delete' ? `Remove ${flagKey} from ${env}` : `Update ${flagKey} in ${env}`}
          confirmLabel={dialog === 'delete' ? 'Remove and publish' : 'Publish'}
        />
      )}
    </div>
  )
}
