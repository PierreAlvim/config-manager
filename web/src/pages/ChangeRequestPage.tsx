import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Check, CircleDot, X } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@/components/ui/alert-dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { PageHeader, ErrorState } from '@/components/common/misc'
import { StatusBadge } from '@/components/common/badges'
import { DiffView } from '@/components/common/DiffView'
import { ReviewDialog } from '@/features/changes/ReviewDialog'
import { useChangeRequest, useCrAction } from '@/api/changeRequests'
import { formatDate } from '@/lib/format'
import { toastError } from '@/lib/errors'

export default function ChangeRequestPage() {
  const { id = '' } = useParams()
  const { data: cr, isLoading, error } = useChangeRequest(id)
  const [review, setReview] = useState<'approve' | 'reject' | null>(null)
  const [cancelOpen, setCancelOpen] = useState(false)
  const cancel = useCrAction(id, 'cancel')

  if (error) return <ErrorState error={error} title="Could not load change request" />
  if (isLoading || !cr) return <Skeleton className="h-96" />

  const approvals = cr.reviews.filter((r) => r.decision === 'approved').length
  const doCancel = async () => {
    try {
      await cancel.mutateAsync(undefined)
      toast.success('Change request cancelled')
      setCancelOpen(false)
    } catch (e) {
      toastError(e, 'Could not cancel')
    }
  }

  return (
    <>
      <PageHeader
        title={
          <span className="flex items-center gap-3">
            {cr.title} <StatusBadge status={cr.status} />
          </span>
        }
        description={
          <>
            <Link className="hover:underline" to={`/namespaces/${cr.namespace}?env=${cr.environment}`}>
              {cr.namespace} / {cr.environment}
            </Link>{' '}
            · opened by <strong>{cr.author}</strong> on {formatDate(cr.createdAt)}
          </>
        }
        actions={
          <>
            {cr.canApprove && (
              <>
                <Button onClick={() => setReview('approve')}>
                  <Check /> Approve
                </Button>
                <Button variant="destructive" onClick={() => setReview('reject')}>
                  <X /> Reject
                </Button>
              </>
            )}
            {cr.canCancel && (
              <Button variant="outline" onClick={() => setCancelOpen(true)}>
                Cancel request
              </Button>
            )}
          </>
        }
      />

      {cr.status === 'conflict' && (
        <div className="mb-4 rounded-lg border border-orange-500/40 bg-orange-500/10 p-3 text-sm">
          The flags in this request changed after it was opened (base version {cr.baseVersion}). Nothing was applied;
          please open a new change request based on the current configuration.
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          {cr.description && (
            <Card>
              <CardHeader>
                <CardTitle>Description</CardTitle>
              </CardHeader>
              <CardContent className="whitespace-pre-wrap text-sm">{cr.description}</CardContent>
            </Card>
          )}
          <section className="space-y-3">
            <h2 className="text-lg font-semibold">Changes ({cr.diff.length})</h2>
            {cr.diff.map((d) => (
              <Card key={d.flagKey + d.type}>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 font-mono text-sm">
                    {d.flagKey}
                    <Badge variant={d.type === 'delete' ? 'destructive' : 'secondary'}>
                      {d.type === 'delete' ? 'remove' : d.before ? 'update' : 'add'}
                    </Badge>
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  <DiffView before={d.before} after={d.after} />
                </CardContent>
              </Card>
            ))}
          </section>
        </div>
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>Details</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid grid-cols-2 gap-y-2 text-sm">
                <dt className="text-muted-foreground">Approvals</dt>
                <dd data-testid="approvals">
                  {approvals} / {cr.requiredApprovals}
                </dd>
                <dt className="text-muted-foreground">Base version</dt>
                <dd>{cr.baseVersion != null ? `v${cr.baseVersion}` : '—'}</dd>
                <dt className="text-muted-foreground">Applied version</dt>
                <dd>{cr.appliedVersion != null ? `v${cr.appliedVersion}` : '—'}</dd>
              </dl>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Reviews</CardTitle>
              <CardDescription>{cr.reviews.length === 0 ? 'No reviews yet.' : `${cr.reviews.length} review(s)`}</CardDescription>
            </CardHeader>
            <CardContent>
              <ol className="relative space-y-4 border-l pl-5">
                <li className="relative">
                  <CircleDot className="absolute -left-[1.78rem] size-4 bg-card text-muted-foreground" />
                  <p className="text-sm font-medium">Opened by {cr.author}</p>
                  <p className="text-xs text-muted-foreground">{formatDate(cr.createdAt)}</p>
                </li>
                {cr.reviews.map((r, i) => (
                  <li key={i} className="relative">
                    {r.decision === 'approved' ? (
                      <Check className="absolute -left-[1.78rem] size-4 rounded-full bg-emerald-500 p-0.5 text-white" />
                    ) : (
                      <X className="absolute -left-[1.78rem] size-4 rounded-full bg-red-500 p-0.5 text-white" />
                    )}
                    <p className="text-sm font-medium">
                      {r.reviewer} {r.decision}
                    </p>
                    {r.comment && <p className="mt-0.5 rounded bg-muted p-2 text-sm">{r.comment}</p>}
                    <p className="text-xs text-muted-foreground">{formatDate(r.at)}</p>
                  </li>
                ))}
              </ol>
            </CardContent>
          </Card>
        </div>
      </div>

      {review && <ReviewDialog id={id} action={review} open onOpenChange={(o) => !o && setReview(null)} />}
      <AlertDialog open={cancelOpen} onOpenChange={setCancelOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Cancel this change request?</AlertDialogTitle>
            <AlertDialogDescription>It will be closed without publishing anything.</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Keep open</AlertDialogCancel>
            <AlertDialogAction onClick={doCancel}>Cancel request</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
