export type DiffKind = 'same' | 'add' | 'remove'
export interface DiffLine {
  kind: DiffKind
  text: string
}
export interface SideRow {
  left: { text: string; kind: 'same' | 'remove' } | null
  right: { text: string; kind: 'same' | 'add' } | null
}

/** Pretty JSON with sorted keys so equal values always render identically. */
export function stableStringify(value: unknown): string {
  return JSON.stringify(sortKeys(value), null, 2) ?? ''
}
function sortKeys(v: unknown): unknown {
  if (Array.isArray(v)) return v.map(sortKeys)
  if (v && typeof v === 'object') {
    return Object.fromEntries(
      Object.entries(v as Record<string, unknown>)
        .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
        .map(([k, x]) => [k, sortKeys(x)]),
    )
  }
  return v
}

/** Line diff via longest common subsequence. */
export function diffLines(a: string, b: string): DiffLine[] {
  const A = a === '' ? [] : a.split('\n')
  const B = b === '' ? [] : b.split('\n')
  const n = A.length
  const m = B.length
  const lcs: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0))
  for (let i = n - 1; i >= 0; i--)
    for (let j = m - 1; j >= 0; j--)
      lcs[i][j] = A[i] === B[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1])
  const out: DiffLine[] = []
  let i = 0
  let j = 0
  while (i < n && j < m) {
    if (A[i] === B[j]) {
      out.push({ kind: 'same', text: A[i] })
      i++
      j++
    } else if (lcs[i + 1][j] >= lcs[i][j + 1]) {
      out.push({ kind: 'remove', text: A[i++] })
    } else {
      out.push({ kind: 'add', text: B[j++] })
    }
  }
  while (i < n) out.push({ kind: 'remove', text: A[i++] })
  while (j < m) out.push({ kind: 'add', text: B[j++] })
  return out
}

/** Aligns a line diff into side-by-side rows (removals paired with following additions). */
export function sideBySide(lines: DiffLine[]): SideRow[] {
  const rows: SideRow[] = []
  let k = 0
  while (k < lines.length) {
    const l = lines[k]
    if (l.kind === 'same') {
      rows.push({ left: { text: l.text, kind: 'same' }, right: { text: l.text, kind: 'same' } })
      k++
      continue
    }
    const removed: string[] = []
    const added: string[] = []
    while (k < lines.length && lines[k].kind !== 'same') {
      if (lines[k].kind === 'remove') removed.push(lines[k].text)
      else added.push(lines[k].text)
      k++
    }
    const len = Math.max(removed.length, added.length)
    for (let x = 0; x < len; x++)
      rows.push({
        left: x < removed.length ? { text: removed[x], kind: 'remove' } : null,
        right: x < added.length ? { text: added[x], kind: 'add' } : null,
      })
  }
  return rows
}

export function diffStats(lines: DiffLine[]) {
  return {
    added: lines.filter((l) => l.kind === 'add').length,
    removed: lines.filter((l) => l.kind === 'remove').length,
  }
}
