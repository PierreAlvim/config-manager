import { describe, expect, it } from 'vitest'
import type { FlagDefinition } from '@/api/types'
import { flagToForm, formToFlag, parseValues, parseVariantValue, renameVariant, rolloutSum } from './flagForm'

describe('parseVariantValue', () => {
  it('parses booleans', () => {
    expect(parseVariantValue('boolean', 'true')).toEqual({ ok: true, value: true })
    expect(parseVariantValue('boolean', 'yes').ok).toBe(false)
  })
  it('parses integers strictly', () => {
    expect(parseVariantValue('integer', '42')).toEqual({ ok: true, value: 42 })
    expect(parseVariantValue('integer', '-7')).toEqual({ ok: true, value: -7 })
    expect(parseVariantValue('integer', '1.5').ok).toBe(false)
    expect(parseVariantValue('integer', '').ok).toBe(false)
  })
  it('parses doubles', () => {
    expect(parseVariantValue('double', '2.5')).toEqual({ ok: true, value: 2.5 })
    expect(parseVariantValue('double', 'abc').ok).toBe(false)
    expect(parseVariantValue('double', '').ok).toBe(false)
  })
  it('keeps strings verbatim, including empty', () => {
    expect(parseVariantValue('string', '')).toEqual({ ok: true, value: '' })
  })
  it('parses JSON objects', () => {
    expect(parseVariantValue('object', '{"a":1}')).toEqual({ ok: true, value: { a: 1 } })
    expect(parseVariantValue('object', '{oops').ok).toBe(false)
    expect(parseVariantValue('object', '5').ok).toBe(false)
  })
})

describe('parseValues', () => {
  it('splits, trims and drops empties', () => {
    expect(parseValues(' a, b ,,c ')).toEqual(['a', 'b', 'c'])
  })
})

const flag: FlagDefinition = {
  type: 'boolean',
  enabled: true,
  variants: { on: true, off: false },
  defaultVariant: 'off',
  rules: [
    { name: 'beta', conditions: [{ attribute: 'tenant', operator: 'in', values: ['acme', 'globex'] }], variant: 'on' },
    { conditions: [], rollout: [{ variant: 'on', weight: 10 }, { variant: 'off', weight: 90 }] },
  ],
  metadata: {},
}

describe('form <-> flag', () => {
  it('round-trips', () => {
    const r = formToFlag('boolean', 'f', flagToForm(flag))
    expect(r.errors).toEqual([])
    expect(r.flag).toEqual(flag)
  })
  it('reports parse and rollout errors', () => {
    const form = flagToForm({ ...flag, type: 'integer', variants: { a: 1, b: 2 }, defaultVariant: 'a', rules: [] })
    form.variants[0].raw = 'x'
    const r = formToFlag('integer', 'f', form)
    expect(r.flag).toBeNull()
    expect(r.errors.join(' ')).toContain("Variant 'a': Must be an integer")
  })
  it('flags rollout not summing to 100', () => {
    const form = flagToForm(flag)
    form.rules[1].rollout[0].weight = '20'
    expect(rolloutSum(form.rules[1].rollout)).toBe(110)
    expect(formToFlag('boolean', 'f', form).errors.join(' ')).toContain('must sum to 100 (got 110)')
  })
  it('renames variant references', () => {
    const f = renameVariant(flagToForm(flag), 'on', 'enabled')
    expect(f.rules[0].variant).toBe('enabled')
    expect(f.rules[1].rollout[0].variant).toBe('enabled')
  })
})
