import { ArrowDown, ArrowUp, Plus, Trash2, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { SimpleSelect } from '@/components/common/misc'
import { OPERATORS, type ConditionOperator } from '@/api/types'
import {
  initialRollout,
  newCondition,
  newRule,
  parseValues,
  rolloutSum,
  uid,
  type ConditionRow,
  type RolloutRow,
  type RuleRow,
} from '@/lib/flagForm'

const NUMERIC: ConditionOperator[] = ['greaterThan', 'greaterThanOrEquals', 'lessThan', 'lessThanOrEquals']

function ConditionEditor({
  c,
  onChange,
  onRemove,
  disabled,
  label,
}: {
  c: ConditionRow
  onChange: (c: ConditionRow) => void
  onRemove: () => void
  disabled?: boolean
  label: string
}) {
  const values = parseValues(c.valuesText)
  const multi = c.operator === 'in' || c.operator === 'notIn'
  return (
    <div className="space-y-1" data-testid="condition-row">
      <div className="grid gap-2 sm:grid-cols-[1fr_11rem_1.4fr_auto]">
        <Input
          aria-label={`${label} attribute`}
          placeholder="attribute (e.g. tenant, targetingKey)"
          value={c.attribute}
          disabled={disabled}
          onChange={(e) => onChange({ ...c, attribute: e.target.value })}
        />
        <SimpleSelect
          aria-label={`${label} operator`}
          value={c.operator}
          disabled={disabled}
          onChange={(v) => onChange({ ...c, operator: v as ConditionOperator })}
          options={OPERATORS.map((o) => ({ value: o }))}
        />
        <Input
          aria-label={`${label} values`}
          placeholder={multi ? 'comma,separated,values' : 'value'}
          value={c.valuesText}
          disabled={disabled}
          onChange={(e) => onChange({ ...c, valuesText: e.target.value })}
        />
        <Button variant="ghost" size="icon" aria-label={`Remove ${label}`} disabled={disabled} onClick={onRemove}>
          <X />
        </Button>
      </div>
      {values.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {values.map((v, i) => (
            <Badge key={i} variant="secondary" className="font-mono text-[11px]">
              {v}
            </Badge>
          ))}
        </div>
      )}
      {values.length === 0 && (
        <p role="alert" className="text-xs text-destructive">
          At least one value is required.
        </p>
      )}
      {NUMERIC.includes(c.operator) && values.some((v) => Number.isNaN(Number(v))) && (
        <p role="alert" className="text-xs text-amber-600">
          Numeric operators compare values as numbers; some values are not numeric.
        </p>
      )}
    </div>
  )
}

function RolloutEditor({
  rows,
  variantNames,
  onChange,
  disabled,
}: {
  rows: RolloutRow[]
  variantNames: string[]
  onChange: (rows: RolloutRow[]) => void
  disabled?: boolean
}) {
  const sum = rolloutSum(rows)
  const ok = sum === 100
  return (
    <div className="space-y-2">
      {rows.map((w, i) => (
        <div key={w.id} className="grid grid-cols-[1fr_7rem_auto] items-center gap-2">
          <SimpleSelect
            aria-label={`Rollout ${i + 1} variant`}
            value={w.variant}
            disabled={disabled}
            onChange={(v) => onChange(rows.map((x) => (x.id === w.id ? { ...x, variant: v } : x)))}
            options={variantNames.map((v) => ({ value: v }))}
          />
          <div className="relative">
            <Input
              aria-label={`Rollout ${i + 1} weight`}
              type="number"
              min={0}
              max={100}
              step={1}
              className="pr-6"
              value={w.weight}
              disabled={disabled}
              onChange={(e) => onChange(rows.map((x) => (x.id === w.id ? { ...x, weight: e.target.value } : x)))}
            />
            <span className="pointer-events-none absolute top-1/2 right-2 -translate-y-1/2 text-xs text-muted-foreground">%</span>
          </div>
          <Button
            variant="ghost"
            size="icon"
            aria-label={`Remove rollout ${i + 1}`}
            disabled={disabled || rows.length <= 1}
            onClick={() => onChange(rows.filter((x) => x.id !== w.id))}
          >
            <X />
          </Button>
        </div>
      ))}
      <div className="flex flex-wrap items-center gap-2">
        {!disabled && (
          <Button
            variant="outline"
            size="xs"
            onClick={() => {
              const used = new Set(rows.map((r) => r.variant))
              const next = variantNames.find((v) => !used.has(v)) ?? variantNames[0] ?? ''
              onChange([...rows, { id: uid(), variant: next, weight: '0' }])
            }}
          >
            <Plus /> Add split
          </Button>
        )}
        <span
          data-testid="rollout-sum"
          role={ok ? undefined : 'alert'}
          className={ok ? 'text-xs text-emerald-600' : 'text-xs font-medium text-destructive'}
        >
          {ok ? 'Total 100%' : `Weights must sum to 100 (currently ${sum})`}
        </span>
      </div>
    </div>
  )
}

export function RulesEditor({
  rules,
  variantNames,
  onChange,
  disabled,
}: {
  rules: RuleRow[]
  variantNames: string[]
  onChange: (rules: RuleRow[]) => void
  disabled?: boolean
}) {
  const patch = (id: string, p: Partial<RuleRow>) => onChange(rules.map((r) => (r.id === id ? { ...r, ...p } : r)))
  const move = (i: number, d: -1 | 1) => {
    const next = [...rules]
    const [r] = next.splice(i, 1)
    next.splice(i + d, 0, r)
    onChange(next)
  }
  return (
    <div className="space-y-3">
      {rules.length === 0 && (
        <p className="rounded-lg border border-dashed p-4 text-center text-sm text-muted-foreground">
          No targeting rules. Everyone gets the default variant.
        </p>
      )}
      {rules.map((r, i) => (
        <Card key={r.id} size="sm" data-testid="rule-card">
          <CardContent className="space-y-3">
            <div className="flex items-center gap-2">
              <Badge variant="outline">#{i + 1}</Badge>
              <Input
                aria-label={`Rule ${i + 1} name`}
                placeholder="Rule name (optional)"
                value={r.name}
                disabled={disabled}
                onChange={(e) => patch(r.id, { name: e.target.value })}
              />
              <Button variant="ghost" size="icon" aria-label={`Move rule ${i + 1} up`} disabled={disabled || i === 0} onClick={() => move(i, -1)}>
                <ArrowUp />
              </Button>
              <Button variant="ghost" size="icon" aria-label={`Move rule ${i + 1} down`} disabled={disabled || i === rules.length - 1} onClick={() => move(i, 1)}>
                <ArrowDown />
              </Button>
              <Button variant="ghost" size="icon" aria-label={`Delete rule ${i + 1}`} disabled={disabled} onClick={() => onChange(rules.filter((x) => x.id !== r.id))}>
                <Trash2 />
              </Button>
            </div>

            <div className="space-y-2">
              <p className="text-xs font-medium text-muted-foreground uppercase">
                When {r.conditions.length === 0 ? '(always matches)' : 'all conditions match'}
              </p>
              {r.conditions.map((c, ci) => (
                <ConditionEditor
                  key={c.id}
                  label={`Rule ${i + 1} condition ${ci + 1}`}
                  c={c}
                  disabled={disabled}
                  onChange={(nc) => patch(r.id, { conditions: r.conditions.map((x) => (x.id === c.id ? nc : x)) })}
                  onRemove={() => patch(r.id, { conditions: r.conditions.filter((x) => x.id !== c.id) })}
                />
              ))}
              {!disabled && (
                <Button variant="outline" size="xs" onClick={() => patch(r.id, { conditions: [...r.conditions, newCondition()] })}>
                  <Plus /> Add condition
                </Button>
              )}
            </div>

            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <p className="text-xs font-medium text-muted-foreground uppercase">Then</p>
                <div className="inline-flex rounded-lg border p-0.5" role="group" aria-label={`Rule ${i + 1} mode`}>
                  {(['variant', 'rollout'] as const).map((m) => (
                    <Button
                      key={m}
                      size="xs"
                      variant={r.mode === m ? 'default' : 'ghost'}
                      aria-pressed={r.mode === m}
                      disabled={disabled}
                      onClick={() =>
                        patch(r.id, {
                          mode: m,
                          rollout: m === 'rollout' && r.rollout.length === 0 ? initialRollout(variantNames) : r.rollout,
                          variant: m === 'variant' && !r.variant ? (variantNames[0] ?? '') : r.variant,
                        })
                      }
                    >
                      {m === 'variant' ? 'Serve variant' : 'Percentage rollout'}
                    </Button>
                  ))}
                </div>
              </div>
              {r.mode === 'variant' ? (
                <SimpleSelect
                  aria-label={`Rule ${i + 1} variant`}
                  className="sm:w-64"
                  value={r.variant}
                  disabled={disabled}
                  onChange={(v) => patch(r.id, { variant: v })}
                  options={variantNames.map((v) => ({ value: v }))}
                  placeholder="Select variant"
                />
              ) : (
                <RolloutEditor rows={r.rollout} variantNames={variantNames} disabled={disabled} onChange={(rollout) => patch(r.id, { rollout })} />
              )}
            </div>
          </CardContent>
        </Card>
      ))}
      {!disabled && (
        <Button variant="outline" size="sm" onClick={() => onChange([...rules, newRule(variantNames)])}>
          <Plus /> Add rule
        </Button>
      )}
    </div>
  )
}
