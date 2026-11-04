import { Link } from 'react-router-dom'
import { MoreHorizontal, Pencil, Trash2 } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Switch } from '@/components/ui/switch'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { TypeBadge } from '@/components/common/badges'
import type { ConfigSnapshot, FlagSummary } from '@/api/types'

export function FlagsTable({
  ns,
  env,
  flags,
  snapshot,
  canEdit,
  onToggle,
  onDelete,
}: {
  ns: string
  env: string
  flags: FlagSummary[]
  snapshot?: ConfigSnapshot
  canEdit: boolean
  onToggle: (flagKey: string, enabled: boolean) => void
  onDelete?: (flagKey: string) => void
}) {
  return (
    <div className="overflow-x-auto rounded-xl border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Flag</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>Enabled</TableHead>
            <TableHead>Default variant</TableHead>
            <TableHead className="text-right">Rules</TableHead>
            <TableHead className="w-12" />
          </TableRow>
        </TableHeader>
        <TableBody>
          {flags.map((f) => {
            const cfg = f.environments[env]
            return (
              <TableRow key={f.key}>
                <TableCell>
                  <Link to={`/namespaces/${ns}/flags/${f.key}?env=${env}`} className="font-mono text-sm font-medium hover:underline">
                    {f.key}
                  </Link>
                  <div className="text-xs text-muted-foreground">{f.name}</div>
                </TableCell>
                <TableCell>
                  <TypeBadge type={f.type} />
                </TableCell>
                <TableCell>
                  {cfg ? (
                    <Switch
                      aria-label={`Toggle ${f.key}`}
                      checked={cfg.enabled}
                      disabled={!canEdit || !snapshot?.flags[f.key]}
                      onCheckedChange={(c) => onToggle(f.key, c)}
                    />
                  ) : (
                    <Badge variant="outline" className="text-muted-foreground">
                      not configured
                    </Badge>
                  )}
                </TableCell>
                <TableCell>{cfg ? <code className="text-xs">{cfg.defaultVariant}</code> : '—'}</TableCell>
                <TableCell className="text-right tabular-nums">{cfg ? cfg.rulesCount : '—'}</TableCell>
                <TableCell>
                  <DropdownMenu>
                    <DropdownMenuTrigger render={<Button variant="ghost" size="icon" aria-label={`Actions for ${f.key}`} />}>
                      <MoreHorizontal />
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end" className="w-44">
                      <DropdownMenuItem render={<Link to={`/namespaces/${ns}/flags/${f.key}?env=${env}`} />}>
                        <Pencil /> {canEdit ? 'Edit' : 'View'}
                      </DropdownMenuItem>
                      {canEdit && onDelete && (
                        <DropdownMenuItem variant="destructive" onClick={() => onDelete(f.key)}>
                          <Trash2 /> Delete flag
                        </DropdownMenuItem>
                      )}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </TableCell>
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}
