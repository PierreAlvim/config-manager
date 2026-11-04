import { Badge } from '@/components/ui/badge'
import type { CrStatus, FlagType, Role } from '@/api/types'
import { cn } from '@/lib/utils'

const STATUS_STYLE: Record<CrStatus, string> = {
  pending: 'bg-amber-500/15 text-amber-700 dark:text-amber-400 border-amber-500/30',
  applied: 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-400 border-emerald-500/30',
  rejected: 'bg-red-500/15 text-red-700 dark:text-red-400 border-red-500/30',
  cancelled: 'bg-muted text-muted-foreground border-border',
  conflict: 'bg-orange-500/15 text-orange-700 dark:text-orange-400 border-orange-500/30',
}
export function StatusBadge({ status }: { status: CrStatus }) {
  return (
    <Badge variant="outline" className={cn('capitalize', STATUS_STYLE[status])} data-testid="status-badge">
      {status}
    </Badge>
  )
}

const TYPE_STYLE: Record<FlagType, string> = {
  boolean: 'bg-sky-500/15 text-sky-700 dark:text-sky-400',
  string: 'bg-violet-500/15 text-violet-700 dark:text-violet-400',
  integer: 'bg-teal-500/15 text-teal-700 dark:text-teal-400',
  double: 'bg-cyan-500/15 text-cyan-700 dark:text-cyan-400',
  object: 'bg-pink-500/15 text-pink-700 dark:text-pink-400',
}
export function TypeBadge({ type }: { type: FlagType }) {
  return (
    <Badge variant="secondary" className={cn('font-mono text-[11px]', TYPE_STYLE[type])}>
      {type}
    </Badge>
  )
}

const ROLE_STYLE: Record<Role, string> = {
  viewer: 'bg-muted text-muted-foreground',
  editor: 'bg-blue-500/15 text-blue-700 dark:text-blue-400',
  approver: 'bg-purple-500/15 text-purple-700 dark:text-purple-400',
  owner: 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-400',
}
export function RoleBadge({ role }: { role?: Role | null }) {
  if (!role) return <Badge variant="outline">no access</Badge>
  return (
    <Badge variant="secondary" className={cn('capitalize', ROLE_STYLE[role])}>
      {role}
    </Badge>
  )
}
