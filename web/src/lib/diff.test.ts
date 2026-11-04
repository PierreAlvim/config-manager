import { describe, expect, it } from 'vitest'
import { diffLines, diffStats, sideBySide, stableStringify } from './diff'

describe('stableStringify', () => {
  it('sorts keys deeply', () => {
    expect(stableStringify({ b: 1, a: { d: 1, c: 2 } })).toBe(stableStringify({ a: { c: 2, d: 1 }, b: 1 }))
  })
})

describe('diffLines', () => {
  it('marks identical text as same', () => {
    expect(diffLines('a\nb', 'a\nb').every((l) => l.kind === 'same')).toBe(true)
  })
  it('detects additions and removals', () => {
    const d = diffLines('a\nb\nc', 'a\nx\nc')
    expect(d).toEqual([
      { kind: 'same', text: 'a' },
      { kind: 'remove', text: 'b' },
      { kind: 'add', text: 'x' },
      { kind: 'same', text: 'c' },
    ])
    expect(diffStats(d)).toEqual({ added: 1, removed: 1 })
  })
  it('handles empty before (new flag)', () => {
    const d = diffLines('', 'a\nb')
    expect(d.map((l) => l.kind)).toEqual(['add', 'add'])
  })
  it('handles empty after (deleted flag)', () => {
    expect(diffLines('a', '').map((l) => l.kind)).toEqual(['remove'])
  })
})

describe('sideBySide', () => {
  it('pairs removals with additions', () => {
    const rows = sideBySide(diffLines('a\nb\nc', 'a\nx\ny\nc'))
    expect(rows).toHaveLength(4)
    expect(rows[1]).toEqual({ left: { text: 'b', kind: 'remove' }, right: { text: 'x', kind: 'add' } })
    expect(rows[2]).toEqual({ left: null, right: { text: 'y', kind: 'add' } })
  })
})
