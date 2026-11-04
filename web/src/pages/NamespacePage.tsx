import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { GitPullRequest, History, Settings } from 'lucide-react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@/components/ui/alert-dialog'
import { EmptyState, ErrorState, PageHeader, TableSkeleton } from '@/components/common/misc'
import { RoleBadge } from '@/components/common/badges'
import { FlagsTable } from '@/features/flags/FlagsTable'
import { NewFlagDialog } from '@/features/flags/NewFlagDialog'
import { PromoteDialog } from '@/features/promote/PromoteDialog'
import { SubmitChangeDialog } from '@/features/changes/SubmitChangeDialog'
import { useDeleteFlag, useEnvConfig, useFlags } from '@/api/flags'
import { useNamespace } from '@/api/namespaces'
import { usePolicies } from '@/api/settings'
import { effectiveRole, roleAtLeast, useEnvironments, useMe } from '@/api/me'
import type { Operation } from '@/api/types'
import { toastError } from '@/lib/errors'

export default function NamespacePage() {
  const { ns = '' } = useParams()
  const [sp, setSp] = useSearchParams()
  const { data: namespace, error: nsError } = useNamespace(ns)
  const { data: envs } = useEnvironments()
  const { data: me } = useMe()
  const { data: flags, isLoading, error } = useFlags(ns)
  const { data: policies } = usePolicies(ns)
  const requested = sp.get('env')
  const env = envs?.find((e) => e.key === requested)?.key ?? envs?.[0]?.key ?? ''
  const { data: snapshot } = useEnvConfig(ns, env)
  const [pending, setPending] = useState<{ op: Operation; title: string } | null>(null)
  const [toDelete, setToDelete] = useState<string | null>(null)
  const del = useDeleteFlag(ns)

  const role = effectiveRole(me, ns, namespace?.myRole)
  const canEdit = roleAtLeast(role, 'editor')
  const policy = policies?.find((p) => p.environment === env)

  if (nsError) return <ErrorState error={nsError} title="Could not load namespace" />

  const onToggle = (flagKey: string, enabled: boolean) => {
    const current = snapshot?.flags[flagKey]
    if (!current) return
    setPending({
      op: { type: 'upsert', flagKey, flag: { ...current, enabled } },
      title: `${enabled ? 'Enable' : 'Disable'} ${flagKey} in ${env}`,
    })
  }

  return (
    <>
      <PageHeader
        title={namespace?.name || ns}
        description={namespace?.description || <code>{ns}</code>}
        actions={
          <>
            <RoleBadge role={role} />
            <Button variant="outline" render={<Link to={`/namespaces/${ns}/history?env=${env}`} />}>
              <History /> History
            </Button>
            <Button variant="outline" render={<Link to={`/change-requests?namespace=${ns}`} />}>
              <GitPullRequest /> Requests
            </Button>
            {role === 'owner' && (
              <Button variant="outline" render={<Link to={`/namespaces/${ns}/settings`} />}>
                <Settings /> Settings
              </Button>
            )}
          </>
        }
      />

      {!envs ? (
        <Skeleton className="h-9 w-64" />
      ) : (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <Tabs value={env} onValueChange={(v) => setSp({ env: v as string }, { replace: true })}>
            <TabsList>
              {envs.map((e) => (
                <TabsTrigger key={e.key} value={e.key}>
                  {e.name || e.key}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="secondary" data-testid="env-version">
              {snapshot ? (snapshot.version > 0 ? `v${snapshot.version}` : 'no version yet') : '…'}
            </Badge>
            {policy &&
              (policy.requireChangeRequest ? (
                <Badge className="bg-amber-500/15 text-amber-700 dark:text-amber-400" data-testid="env-policy">
                  Change request required · {policy.requiredApprovals} approval{policy.requiredApprovals > 1 ? 's' : ''}
                </Badge>
              ) : (
                <Badge variant="outline" data-testid="env-policy">
                  Direct publish
                </Badge>
              ))}
            {canEdit && envs && flags && <PromoteDialog ns={ns} toEnv={env} envs={envs} flags={flags} policy={policy} />}
            {canEdit && <NewFlagDialog ns={ns} env={env} />}
          </div>
        </div>
      )}

      {error ? (
        <ErrorState error={error} title="Could not load flags" />
      ) : isLoading || !envs ? (
        <TableSkeleton cols={5} />
      ) : !flags || flags.length === 0 ? (
        <EmptyState
          title="No flags yet"
          description={canEdit ? 'Create your first flag, then configure it per environment.' : 'This namespace has no flags.'}
          action={canEdit ? <NewFlagDialog ns={ns} env={env} /> : undefined}
        />
      ) : (
        <FlagsTable ns={ns} env={env} flags={flags} snapshot={snapshot} canEdit={canEdit} onToggle={onToggle} onDelete={setToDelete} />
      )}

      {pending && (
        <SubmitChangeDialog
          ns={ns}
          env={env}
          policy={policy}
          operations={[pending.op]}
          defaultTitle={pending.title}
          open
          onOpenChange={(o) => !o && setPending(null)}
        />
      )}
      <AlertDialog open={!!toDelete} onOpenChange={(o) => !o && setToDelete(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete flag {toDelete}?</AlertDialogTitle>
            <AlertDialogDescription>
              Removes the flag definition. This only works when the flag is not configured in any environment.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={async () => {
                try {
                  await del.mutateAsync(toDelete!)
                  toast.success('Flag deleted')
                } catch (e) {
                  toastError(e, 'Could not delete flag')
                }
                setToDelete(null)
              }}
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
