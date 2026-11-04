import { Link, useSearchParams } from 'react-router-dom'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { PageHeader, EmptyState, ErrorState, SimpleSelect, TableSkeleton } from '@/components/common/misc'
import { StatusBadge } from '@/components/common/badges'
import { useChangeRequests } from '@/api/changeRequests'
import { useNamespaces } from '@/api/namespaces'
import { useEnvironments } from '@/api/me'
import { formatDate } from '@/lib/format'

const STATUSES = ['pending', 'applied', 'rejected', 'cancelled', 'conflict']

export default function ChangeRequestsPage() {
  const [sp, setSp] = useSearchParams()
  const namespace = sp.get('namespace') ?? 'all'
  const environment = sp.get('environment') ?? 'all'
  const status = sp.get('status') ?? 'all'
  const set = (k: string) => (v: string) => {
    const n = new URLSearchParams(sp)
    if (v === 'all') n.delete(k)
    else n.set(k, v)
    setSp(n, { replace: true })
  }
  const { data: namespaces } = useNamespaces()
  const { data: envs } = useEnvironments()
  const { data, isLoading, error } = useChangeRequests({
    namespace: namespace === 'all' ? undefined : namespace,
    environment: environment === 'all' ? undefined : environment,
    status: status === 'all' ? undefined : status,
  })
  return (
    <>
      <PageHeader title="Change requests" description="Review and approve changes before they are published." />
      <div className="mb-4 flex flex-wrap gap-2">
        <SimpleSelect className="w-44" aria-label="Namespace filter" value={namespace} onChange={set('namespace')}
          options={[{ value: 'all', label: 'All namespaces' }, ...(namespaces ?? []).map((n) => ({ value: n.key, label: n.name || n.key }))]} />
        <SimpleSelect className="w-40" aria-label="Environment filter" value={environment} onChange={set('environment')}
          options={[{ value: 'all', label: 'All environments' }, ...(envs ?? []).map((e) => ({ value: e.key, label: e.name || e.key }))]} />
        <SimpleSelect className="w-40" aria-label="Status filter" value={status} onChange={set('status')}
          options={[{ value: 'all', label: 'All statuses' }, ...STATUSES.map((s) => ({ value: s, label: s }))]} />
      </div>
      {error ? (
        <ErrorState error={error} title="Could not load change requests" />
      ) : isLoading ? (
        <TableSkeleton cols={6} />
      ) : !data || data.length === 0 ? (
        <EmptyState title="No change requests" description="Nothing matches the current filters." />
      ) : (
        <div className="overflow-x-auto rounded-xl border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Title</TableHead>
                <TableHead>Scope</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Author</TableHead>
                <TableHead>Created</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((cr) => (
                <TableRow key={cr.id}>
                  <TableCell className="font-medium">
                    <Link to={`/change-requests/${cr.id}`} className="hover:underline">
                      {cr.title}
                    </Link>
                  </TableCell>
                  <TableCell className="whitespace-nowrap">
                    {cr.namespace} <span className="text-muted-foreground">/ {cr.environment}</span>
                  </TableCell>
                  <TableCell>
                    <StatusBadge status={cr.status} />
                  </TableCell>
                  <TableCell>{cr.author}</TableCell>
                  <TableCell className="whitespace-nowrap text-muted-foreground">{formatDate(cr.createdAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </>
  )
}
