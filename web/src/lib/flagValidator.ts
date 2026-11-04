import type { FlagDefinition, FlagType, JsonValue } from '@/api/types'

const KEY_RE = /^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?$/

export function isValidKey(key: string | null | undefined): boolean {
  return !!key && KEY_RE.test(key)
}

export function valueMatchesType(type: FlagType, v: JsonValue): boolean {
  switch (type) {
    case 'boolean':
      return typeof v === 'boolean'
    case 'string':
      return typeof v === 'string'
    case 'integer':
      return typeof v === 'number' && Number.isInteger(v)
    case 'double':
      return typeof v === 'number'
    case 'object':
      return typeof v === 'object' && v !== null
  }
}

/** Mirrors ConfigManager.Contracts.FlagValidator.Validate. */
export function validateFlag(flagKey: string, flag: FlagDefinition): string[] {
  const errors: string[] = []
  if (!isValidKey(flagKey)) errors.push(`Invalid flag key '${flagKey}'.`)
  const names = Object.keys(flag.variants)
  if (names.length === 0) errors.push('At least one variant is required.')
  for (const [name, value] of Object.entries(flag.variants)) {
    if (!name.trim()) errors.push('Variant names cannot be empty.')
    if (!valueMatchesType(flag.type, value))
      errors.push(`Variant '${name}' value does not match type '${flag.type}'.`)
  }
  if (!(flag.defaultVariant in flag.variants)) errors.push(`Default variant '${flag.defaultVariant}' is not defined.`)

  flag.rules.forEach((rule, i) => {
    const label = `Rule #${i + 1}${rule.name ? ` ('${rule.name}')` : ''}`
    const hasVariant = rule.variant != null
    const hasRollout = !!rule.rollout && rule.rollout.length > 0
    if (hasVariant === hasRollout) errors.push(`${label}: exactly one of 'variant' or 'rollout' must be set.`)
    if (hasVariant && !(rule.variant! in flag.variants)) errors.push(`${label}: variant '${rule.variant}' is not defined.`)
    if (hasRollout) {
      for (const wv of rule.rollout!) {
        if (!(wv.variant in flag.variants)) errors.push(`${label}: rollout variant '${wv.variant}' is not defined.`)
        if (wv.weight < 0) errors.push(`${label}: rollout weights cannot be negative.`)
      }
      const sum = rule.rollout!.reduce((s, w) => s + w.weight, 0)
      if (sum !== 100) errors.push(`${label}: rollout weights must sum to 100 (got ${sum}).`)
    }
    for (const c of rule.conditions) {
      if (!c.attribute.trim()) errors.push(`${label}: condition attribute cannot be empty.`)
      if (c.values.length === 0) errors.push(`${label}: condition on '${c.attribute}' requires at least one value.`)
    }
  })
  return errors
}
