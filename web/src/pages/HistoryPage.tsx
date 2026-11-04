import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, Eye, GitCompare, Undo2 } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@/components/ui/alert-dialog'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { EmptyState, ErrorState, PageHeader, TableSkeleton } from '@/components/common/misc'
import { DiffView } from '@/components/common/DiffView'
import { useEnvConfig } from '@/api/flags'
import { useRollback, useVersion, useVersions } from '@/api/versions'
import { useNamespace } from '@/api/namespaces'
import { effectiveRole, roleAtLeast, useEnvironments, useMe } from '@/api/me'
import { stableStringify } from '@/lib/diff'
import { formatDate } from '@/lib/format'
import { toastError } from '@/lib/errors'
import { useOutcomeToast } from '@/lib/outcome'

function SnapshotDialog({ ns, env, version, mode, onClose }: { ns: string; env: string; version: number; mode: 'view' | 'compare'; onClose: () => void }) {
  const { data, isLoading, error } = useVersion(ns, env, version)
  const { data: current } = useEnvConfig(ns, env)
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-auto sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle>{mode === 'view' ? `Snapshot v${version}` : `v${version} vs current (v${current?.version ?? '…'})`}</DialogTitle>
          <DialogDescription>
            {ns} / {env}
          </DialogDescription>
        </DialogHeader>
        {error ? (
          <ErrorState error={error} />
        ) : isLoading || !data ? (
          <Skeleton className="h-64" />
        ) : mode === 'view' ? (
          <pre className="max-h-[60vh] overflow-auto rounded-lg bg-muted p-3 text-xs">{stableStringify(data.snapshot)}</pre>
        ) : (
          <DiffView before={data.snapshot.flags} after={current?.flags ?? {}} beforeLabel={`v${version}`} afterLabel={`current (v${current?.version ?? '?'})`} />
        )}
      </DialogContent>
    </Dialog>
  )
}

export default function HistoryPage() {
  const { ns = '' } = useParams()
  const [sp, setSp] = useSearchParams()
  const { data: envs } = useEnvironments()
  const env = envs?.find((e) => e.key === sp.get('env'))?.key ?? envs?.[0]?.key ?? ''
  const { data: versions, isLoading, error } = useVersions(ns, env)
  const { data: namespace } = useNamespace(ns)
  const { data: me } = useMe()
  const [view, setView] = useState<{ v: number; mode: 'view' | 'compare' } | null>(null)
  const [rb, setRb] = useState<number | null>(null)
  const [reason, setReason] = useState('')
  const rollback = useRollback(ns, env)
  const toastOutcome = useOutcomeToast()
  const canEdit = roleAtLeast(effectiveRole(me, ns, namespace?.myRole), 'editor')
  const latest = versions?.[0]?.version

  const doRollback = async () => {
    if (rb === null) return
    try {
      toastOutcome(await rollback.mutateAsync({ version: rb, reason: reason.trim() || undefined }))
      setRb(null)
      setReason('')
    } catch (e) {
      toastError(e, 'Rollback failed')
      setRb(null)
    }
  }

  return (
    <>
      <Button variant="ghost" size="sm" className="mb-2 -ml-2" render={<Link to={`/namespaces/${ns}?env=${env}`} />}>
        <ArrowLeft /> {namespace?.name ?? ns}
      </Button>
      <PageHeader title="Version history" description="Every published change creates an immutable version." />
      {envs && (
        <Tabs value={env} onValueChange={(v) => setSp({ env: v as string }, { replace: true })} className="mb-4">
          <TabsList>
            {envs.map((e) => (
              <TabsTrigger key={e.key} value={e.key}>
                {e.name || e.key}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
      )}
      {error ? (
        <ErrorState error={error} title="Could not load versions" />
      ) : isLoading || !envs ? (
        <TableSkeleton cols={5} />
      ) : !versions || versions.length === 0 ? (
        <EmptyState title="No versions yet" description={`Nothing has been published to ${env}.`} />
      ) : (
        <div className="overflow-x-auto rounded-xl border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Version</TableHead>
                <TableHead>Published</TableHead>
                <TableHead>By</TableHead>
                <TableHead>Message</TableHead>
                <TableHead>Change request</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {versions.map((v) => (
                <TableRow key={v.version}>
                  <TableCell>
                    <Badge variant={v.version === latest ? 'default' : 'secondary'}>v{v.version}</Badge>
                    {v.version === latest && <span className="ml-2 text-xs text-muted-foreground">current</span>}
                  </TableCell>
                  <TableCell className="whitespace-nowrap">{formatDate(v.publishedAt)}</TableCell>
                  <TableCell>{v.publishedBy}</TableCell>
                  <TableCell className="max-w-xs truncate">{v.message}</TableCell>
                  <TableCell>
                    {v.changeRequestId ? (
                      <Link className="text-sm underline" to={`/change-requests/${v.changeRequestId}`}>
                        view
                      </Link>
                    ) : (
                      '—'
                    )}
                  </TableCell>
                  <TableCell className="text-right whitespace-nowrap">
                    <Button variant="ghost" size="sm" onClick={() => setView({ v: v.version, mode: 'view' })}>
                      <Eye /> Snapshot
                    </Button>
                    {v.version !== latest && (
                      <Button variant="ghost" size="sm" onClick={() => setView({ v: v.version, mode: 'compare' })}>
                        <GitCompare /> Compare
                      </Button>
                    )}
                    {canEdit && v.version !== latest && (
                      <Button variant="ghost" size="sm" onClick={() => setRb(v.version)}>
                        <Undo2 /> Rollback
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {view && <SnapshotDialog ns={ns} env={env} version={view.v} mode={view.mode} onClose={() => setView(null)} />}
      <AlertDialog open={rb !== null} onOpenChange={(o) => !o && setRb(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Roll back {env} to v{rb}?</AlertDialogTitle>
            <AlertDialogDescription>
              This creates a NEW version restoring the flags of v{rb}. If the environment requires review, a change request is opened instead.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <Input placeholder="Reason (optional)" aria-label="Rollback reason" value={reason} onChange={(e) => setReason(e.target.value)} />
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={doRollback} disabled={rollback.isPending}>
              Rollback
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
