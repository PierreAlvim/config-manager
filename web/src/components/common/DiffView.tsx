import { useMemo } from 'react'
import { diffLines, diffStats, sideBySide, stableStringify } from '@/lib/diff'
import { cn } from '@/lib/utils'

export function DiffView({
  before,
  after,
  beforeLabel = 'Before',
  afterLabel = 'After',
}: {
  before: unknown
  after: unknown
  beforeLabel?: string
  afterLabel?: string
}) {
  const { rows, stats, identical } = useMemo(() => {
    const a = before == null ? '' : stableStringify(before)
    const b = after == null ? '' : stableStringify(after)
    const lines = diffLines(a, b)
    return { rows: sideBySide(lines), stats: diffStats(lines), identical: a === b }
  }, [before, after])
  return (
    <div className="overflow-hidden rounded-lg border text-xs" data-testid="diff-view">
      <div className="grid grid-cols-2 border-b bg-muted/50 px-3 py-1.5 font-medium">
        <span>{beforeLabel}</span>
        <span className="flex items-center justify-between">
          {afterLabel}
          <span className="font-mono text-[11px]">
            <span className="text-emerald-600">+{stats.added}</span> <span className="text-red-600">-{stats.removed}</span>
          </span>
        </span>
      </div>
      {identical ? (
        <div className="p-3 text-muted-foreground">No differences.</div>
      ) : (
        <div className="max-h-[28rem] overflow-auto">
          <div className="grid min-w-[40rem] grid-cols-2 font-mono leading-5">
            {rows.map((r, i) => (
              <div key={i} className="contents">
                <pre
                  className={cn(
                    'whitespace-pre-wrap break-all border-r px-3',
                    r.left?.kind === 'remove' && 'bg-red-500/15 text-red-800 dark:text-red-300',
                    !r.left && 'bg-muted/30',
                  )}
                  data-kind={r.left?.kind ?? 'empty'}
                >
                  {r.left?.text ?? ' '}
                </pre>
                <pre
                  className={cn(
                    'whitespace-pre-wrap break-all px-3',
                    r.right?.kind === 'add' && 'bg-emerald-500/15 text-emerald-800 dark:text-emerald-300',
                    !r.right && 'bg-muted/30',
                  )}
                  data-kind={r.right?.kind ?? 'empty'}
                >
                  {r.right?.text ?? ' '}
                </pre>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
