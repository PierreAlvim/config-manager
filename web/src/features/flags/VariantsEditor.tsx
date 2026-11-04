import { Plus, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { SimpleSelect } from '@/components/common/misc'
import type { FlagType } from '@/api/types'
import { defaultRawFor, parseVariantValue, uid, type VariantRow } from '@/lib/flagForm'

export function VariantValueInput({
  type,
  value,
  onChange,
  disabled,
  label,
}: {
  type: FlagType
  value: string
  onChange: (v: string) => void
  disabled?: boolean
  label: string
}) {
  const parsed = parseVariantValue(type, value)
  const error = parsed.ok ? null : parsed.error
  let input
  switch (type) {
    case 'boolean':
      input = (
        <SimpleSelect
          aria-label={label}
          value={value === 'true' || value === 'false' ? value : ''}
          onChange={onChange}
          disabled={disabled}
          options={[{ value: 'true' }, { value: 'false' }]}
          placeholder="Select…"
        />
      )
      break
    case 'object':
      input = (
        <Textarea
          aria-label={label}
          aria-invalid={!!error}
          className="min-h-20 font-mono text-xs"
          value={value}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
          spellCheck={false}
        />
      )
      break
    default:
      input = (
        <Input
          aria-label={label}
          aria-invalid={!!error}
          inputMode={type === 'string' ? undefined : 'decimal'}
          value={value}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
          placeholder={type === 'string' ? '(empty string)' : type === 'integer' ? '0' : '0.0'}
        />
      )
  }
  return (
    <div className="space-y-1">
      {input}
      {error && value !== '' && (
        <p role="alert" className="text-xs text-destructive">
          {error}
        </p>
      )}
      {error && value === '' && type !== 'string' && (
        <p role="alert" className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}

export function VariantsEditor({
  type,
  rows,
  onChange,
  onRename,
  onRemove,
  disabled,
}: {
  type: FlagType
  rows: VariantRow[]
  onChange: (rows: VariantRow[]) => void
  onRename?: (from: string, to: string) => void
  onRemove?: (name: string) => void
  disabled?: boolean
}) {
  const update = (id: string, patch: Partial<VariantRow>) => {
    const old = rows.find((r) => r.id === id)
    if (old && patch.name !== undefined && onRename) onRename(old.name, patch.name)
    onChange(rows.map((r) => (r.id === id ? { ...r, ...patch } : r)))
  }
  const names = rows.map((r) => r.name.trim())
  return (
    <div className="space-y-3">
      {rows.map((r, i) => (
        <div key={r.id} className="grid gap-2 sm:grid-cols-[12rem_1fr_auto] sm:items-start" data-testid="variant-row">
          <div className="space-y-1">
            <Input
              aria-label={`Variant ${i + 1} name`}
              placeholder="variant name"
              value={r.name}
              disabled={disabled}
              onChange={(e) => update(r.id, { name: e.target.value })}
              aria-invalid={!r.name.trim() || names.indexOf(r.name.trim()) !== i}
            />
            {names.indexOf(r.name.trim()) !== i && r.name.trim() && (
              <p role="alert" className="text-xs text-destructive">
                Duplicate name
              </p>
            )}
          </div>
          <VariantValueInput
            label={`Variant ${i + 1} value`}
            type={type}
            value={r.raw}
            disabled={disabled}
            onChange={(raw) => update(r.id, { raw })}
          />
          <Button
            variant="ghost"
            size="icon"
            aria-label={`Remove variant ${r.name || i + 1}`}
            disabled={disabled || rows.length <= 1}
            onClick={() => {
              onRemove?.(r.name)
              onChange(rows.filter((x) => x.id !== r.id))
            }}
          >
            <Trash2 />
          </Button>
        </div>
      ))}
      {!disabled && (
        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            let n = rows.length + 1
            while (names.includes(`variant-${n}`)) n++
            onChange([...rows, { id: uid(), name: `variant-${n}`, raw: defaultRawFor(type, rows.length) }])
          }}
        >
          <Plus /> Add variant
        </Button>
      )}
    </div>
  )
}
