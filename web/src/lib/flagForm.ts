import type {
  ConditionOperator,
  FlagDefinition,
  FlagType,
  JsonValue,
  TargetingRule,
} from '@/api/types'
import { validateFlag } from './flagValidator'

let seq = 0
export const uid = () => `r${++seq}`

export interface VariantRow {
  id: string
  name: string
  raw: string
}
export interface ConditionRow {
  id: string
  attribute: string
  operator: ConditionOperator
  valuesText: string
}
export interface RolloutRow {
  id: string
  variant: string
  weight: string
}
export interface RuleRow {
  id: string
  name: string
  conditions: ConditionRow[]
  mode: 'variant' | 'rollout'
  variant: string
  rollout: RolloutRow[]
}
export interface FlagForm {
  enabled: boolean
  variants: VariantRow[]
  defaultVariant: string
  rules: RuleRow[]
  metadata: Record<string, string>
}

export type ParsedValue = { ok: true; value: JsonValue } | { ok: false; error: string }

/** Parses the textual editor input of a variant value according to the flag type. */
export function parseVariantValue(type: FlagType, raw: string): ParsedValue {
  switch (type) {
    case 'boolean':
      if (raw === 'true') return { ok: true, value: true }
      if (raw === 'false') return { ok: true, value: false }
      return { ok: false, error: 'Must be true or false' }
    case 'string':
      return { ok: true, value: raw }
    case 'integer': {
      if (!/^-?\d+$/.test(raw.trim())) return { ok: false, error: 'Must be an integer' }
      const n = Number(raw)
      if (!Number.isSafeInteger(n)) return { ok: false, error: 'Integer out of range' }
      return { ok: true, value: n }
    }
    case 'double': {
      const t = raw.trim()
      if (t === '' || Number.isNaN(Number(t)) || !Number.isFinite(Number(t))) return { ok: false, error: 'Must be a number' }
      return { ok: true, value: Number(t) }
    }
    case 'object': {
      try {
        const v = JSON.parse(raw) as JsonValue
        if (typeof v !== 'object' || v === null) return { ok: false, error: 'Must be a JSON object or array' }
        return { ok: true, value: v }
      } catch (e) {
        return { ok: false, error: `Invalid JSON: ${(e as Error).message}` }
      }
    }
  }
}

export function valueToRaw(type: FlagType, v: JsonValue): string {
  if (type === 'string') return typeof v === 'string' ? v : JSON.stringify(v)
  if (type === 'object') return JSON.stringify(v, null, 2)
  return String(v)
}

export function defaultRawFor(type: FlagType, index = 0): string {
  switch (type) {
    case 'boolean':
      return index === 0 ? 'true' : 'false'
    case 'string':
      return ''
    case 'integer':
    case 'double':
      return String(index)
    case 'object':
      return '{}'
  }
}

export function defaultFlagForm(type: FlagType): FlagForm {
  if (type === 'boolean') {
    return {
      enabled: true,
      variants: [
        { id: uid(), name: 'on', raw: 'true' },
        { id: uid(), name: 'off', raw: 'false' },
      ],
      defaultVariant: 'off',
      rules: [],
      metadata: {},
    }
  }
  return {
    enabled: true,
    variants: [{ id: uid(), name: 'default', raw: defaultRawFor(type) }],
    defaultVariant: 'default',
    rules: [],
    metadata: {},
  }
}

export function flagToForm(flag: FlagDefinition): FlagForm {
  return {
    enabled: flag.enabled,
    variants: Object.entries(flag.variants).map(([name, v]) => ({ id: uid(), name, raw: valueToRaw(flag.type, v) })),
    defaultVariant: flag.defaultVariant,
    metadata: flag.metadata ?? {},
    rules: flag.rules.map((r) => ({
      id: uid(),
      name: r.name ?? '',
      conditions: r.conditions.map((c) => ({
        id: uid(),
        attribute: c.attribute,
        operator: c.operator,
        valuesText: c.values.join(', '),
      })),
      mode: r.rollout && r.rollout.length > 0 ? 'rollout' : 'variant',
      variant: r.variant ?? '',
      rollout: (r.rollout ?? []).map((w) => ({ id: uid(), variant: w.variant, weight: String(w.weight) })),
    })),
  }
}

export function parseValues(text: string): string[] {
  return text
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
}

export function rolloutSum(rows: RolloutRow[]): number {
  return rows.reduce((s, r) => s + (Number.parseInt(r.weight, 10) || 0), 0)
}

export interface BuildResult {
  flag: FlagDefinition | null
  errors: string[]
}

/** Converts the form to a FlagDefinition and validates it (parse errors + FlagValidator mirror). */
export function formToFlag(type: FlagType, flagKey: string, form: FlagForm): BuildResult {
  const errors: string[] = []
  const variants: Record<string, JsonValue> = {}
  for (const row of form.variants) {
    const name = row.name.trim()
    if (!name) {
      errors.push('Variant names cannot be empty.')
      continue
    }
    if (name in variants) {
      errors.push(`Duplicate variant name '${name}'.`)
      continue
    }
    const p = parseVariantValue(type, row.raw)
    if (!p.ok) errors.push(`Variant '${name}': ${p.error}.`)
    else variants[name] = p.value
  }
  const rules: TargetingRule[] = form.rules.map((r) => {
    const rule: TargetingRule = {
      conditions: r.conditions.map((c) => ({
        attribute: c.attribute.trim(),
        operator: c.operator,
        values: parseValues(c.valuesText),
      })),
    }
    if (r.name.trim()) rule.name = r.name.trim()
    if (r.mode === 'variant') rule.variant = r.variant
    else
      rule.rollout = r.rollout.map((w) => ({ variant: w.variant, weight: Number.parseInt(w.weight, 10) || 0 }))
    return rule
  })
  const flag: FlagDefinition = {
    type,
    enabled: form.enabled,
    variants,
    defaultVariant: form.defaultVariant,
    rules,
    metadata: form.metadata,
  }
  // variant parse failures would otherwise be reported again as "not defined" – drop those duplicates
  const validation = validateFlag(flagKey, flag).filter(
    (e) => !(e.startsWith('Variant ') && e.includes('does not match')),
  )
  const all = [...errors, ...validation]
  return { flag: all.length ? null : flag, errors: all }
}

/** Renames a variant everywhere it is referenced (default variant, rule targets, rollouts). */
export function renameVariant(form: FlagForm, from: string, to: string): FlagForm {
  if (from === to) return form
  return {
    ...form,
    defaultVariant: form.defaultVariant === from ? to : form.defaultVariant,
    rules: form.rules.map((r) => ({
      ...r,
      variant: r.variant === from ? to : r.variant,
      rollout: r.rollout.map((w) => (w.variant === from ? { ...w, variant: to } : w)),
    })),
  }
}

/** Drops references to a variant that was removed. */
export function removeVariantRefs(form: FlagForm, name: string): FlagForm {
  const fallback = form.variants.find((v) => v.name !== name)?.name ?? ''
  return {
    ...form,
    defaultVariant: form.defaultVariant === name ? fallback : form.defaultVariant,
    rules: form.rules.map((r) => ({
      ...r,
      variant: r.variant === name ? fallback : r.variant,
      rollout: r.rollout.filter((w) => w.variant !== name),
    })),
  }
}

export function newRule(variantNames: string[]): RuleRow {
  return { id: uid(), name: '', conditions: [], mode: 'variant', variant: variantNames[0] ?? '', rollout: [] }
}
export function newCondition(): ConditionRow {
  return { id: uid(), attribute: '', operator: 'equals', valuesText: '' }
}
export function initialRollout(variantNames: string[]): RolloutRow[] {
  return variantNames.slice(0, 2).map((v, i) => ({ id: uid(), variant: v, weight: i === 0 ? '100' : '0' }))
}
