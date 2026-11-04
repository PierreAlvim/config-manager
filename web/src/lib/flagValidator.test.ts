import { describe, expect, it } from 'vitest'
import type { FlagDefinition } from '@/api/types'
import { isValidKey, validateFlag } from './flagValidator'

const base = (): FlagDefinition => ({
  type: 'boolean',
  enabled: true,
  variants: { on: true, off: false },
  defaultVariant: 'off',
  rules: [],
})

describe('isValidKey', () => {
  it.each([
    ['new-checkout', true],
    ['a', true],
    ['a.b_c-d', true],
    ['-bad', false],
    ['Bad', false],
    ['trailing-', false],
    ['', false],
    ['x'.repeat(65), false],
  ])('%s -> %s', (k, ok) => expect(isValidKey(k)).toBe(ok))
})

describe('validateFlag', () => {
  it('accepts a valid flag', () => {
    expect(validateFlag('f', base())).toEqual([])
  })
  it('requires variants and a defined default', () => {
    const f = { ...base(), variants: {}, defaultVariant: 'x' }
    const errs = validateFlag('f', f)
    expect(errs).toContain('At least one variant is required.')
    expect(errs).toContain("Default variant 'x' is not defined.")
  })
  it('checks variant value types', () => {
    const f: FlagDefinition = { ...base(), type: 'integer', variants: { a: 1.5, b: 2 }, defaultVariant: 'b' }
    expect(validateFlag('f', f)).toEqual(["Variant 'a' value does not match type 'integer'."])
    expect(validateFlag('f', { ...f, type: 'double' })).toEqual([])
    expect(validateFlag('f', { ...f, type: 'object' })).toHaveLength(2)
  })
  it('requires exactly one of variant/rollout', () => {
    const f = { ...base(), rules: [{ conditions: [] }, { conditions: [], variant: 'on', rollout: [{ variant: 'on', weight: 100 }] }] }
    const errs = validateFlag('f', f)
    expect(errs.filter((e) => e.includes("exactly one of 'variant' or 'rollout'"))).toHaveLength(2)
  })
  it('validates rollout sums and unknown variants', () => {
    const f = {
      ...base(),
      rules: [{ name: 'r', conditions: [], rollout: [{ variant: 'on', weight: 10 }, { variant: 'zzz', weight: 80 }] }],
    }
    const errs = validateFlag('f', f)
    expect(errs).toContain("Rule #1 ('r'): rollout variant 'zzz' is not defined.")
    expect(errs).toContain("Rule #1 ('r'): rollout weights must sum to 100 (got 90).")
  })
  it('validates conditions', () => {
    const f = { ...base(), rules: [{ conditions: [{ attribute: ' ', operator: 'in' as const, values: [] }], variant: 'on' }] }
    const errs = validateFlag('f', f)
    expect(errs.some((e) => e.includes('attribute cannot be empty'))).toBe(true)
    expect(errs.some((e) => e.includes('requires at least one value'))).toBe(true)
  })
})
