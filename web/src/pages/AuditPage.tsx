import { useState } from 'react'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { PageHeader, EmptyState, ErrorState, SimpleSelect, TableSkeleton } from '@/components/common/misc'
import { Badge } from '@/components/ui/badge'
import { useAudit } from '@/api/settings'
import { useNamespaces } from '@/api/namespaces'
import { formatDate } from '@/lib/format'

export default function AuditPage() {
  const [ns, setNs] = useState('all')
  const { data: namespaces } = useNamespaces()
  const { data, isLoading, error } = useAudit(ns === 'all' ? undefined : ns, 200)
  return (
    <>
      <PageHeader
        title="Audit log"
        description="Who changed what, and when."
        actions={
          <SimpleSelect
            className="w-48"
            value={ns}
            onChange={setNs}
            aria-label="Namespace filter"
            options={[{ value: 'all', label: 'All namespaces' }, ...(namespaces ?? []).map((n) => ({ value: n.key, label: n.name || n.key }))]}
          />
        }
      />
      {error ? (
        <ErrorState error={error} title="Could not load audit log" />
      ) : isLoading ? (
        <TableSkeleton cols={5} />
      ) : !data || data.length === 0 ? (
        <EmptyState title="No audit entries" />
      ) : (
        <div className="overflow-x-auto rounded-xl border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>When</TableHead>
                <TableHead>Actor</TableHead>
                <TableHead>Action</TableHead>
                <TableHead>Scope</TableHead>
                <TableHead>Details</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((a, i) => (
                <TableRow key={i}>
                  <TableCell className="whitespace-nowrap text-muted-foreground">{formatDate(a.at)}</TableCell>
                  <TableCell>{a.actor}</TableCell>
                  <TableCell>
                    <Badge variant="secondary" className="font-mono text-[11px]">
                      {a.action}
                    </Badge>
                  </TableCell>
                  <TableCell className="whitespace-nowrap">
                    {a.namespace ?? '—'}
                    {a.environment ? <span className="text-muted-foreground"> / {a.environment}</span> : null}
                  </TableCell>
                  <TableCell className="max-w-md truncate text-muted-foreground" title={a.details ?? ''}>
                    {a.details}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </>
  )
}
