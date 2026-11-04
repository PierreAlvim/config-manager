import { Link } from 'react-router-dom'
import { FolderKanban, ArrowRight } from 'lucide-react'
import { Card, CardDescription, CardHeader, CardTitle, CardAction } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { PageHeader, EmptyState, ErrorState } from '@/components/common/misc'
import { RoleBadge } from '@/components/common/badges'
import { CreateNamespaceDialog } from '@/features/CreateNamespaceDialog'
import { useNamespaces } from '@/api/namespaces'
import { effectiveRole, useMe } from '@/api/me'

export default function NamespacesPage() {
  const { data, isLoading, error } = useNamespaces()
  const { data: me } = useMe()
  return (
    <>
      <PageHeader
        title="Namespaces"
        description="Pick a namespace to manage its flags across environments."
        actions={me?.isAdmin ? <CreateNamespaceDialog /> : undefined}
      />
      {error ? (
        <ErrorState error={error} title="Could not load namespaces" />
      ) : isLoading ? (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-28" />
          ))}
        </div>
      ) : data && data.length > 0 ? (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {data.map((ns) => (
            <Link key={ns.key} to={`/namespaces/${ns.key}`} className="group">
              <Card className="h-full transition-shadow group-hover:shadow-md">
                <CardHeader>
                  <CardTitle className="flex items-center gap-2">
                    <FolderKanban className="size-4 text-muted-foreground" />
                    {ns.name || ns.key}
                  </CardTitle>
                  <CardDescription>
                    <code className="text-xs">{ns.key}</code>
                    {ns.description ? <span className="mt-1 block line-clamp-2">{ns.description}</span> : null}
                  </CardDescription>
                  <CardAction className="flex items-center gap-2">
                    <RoleBadge role={effectiveRole(me, ns.key, ns.myRole)} />
                    <ArrowRight className="size-4 text-muted-foreground transition-transform group-hover:translate-x-0.5" />
                  </CardAction>
                </CardHeader>
              </Card>
            </Link>
          ))}
        </div>
      ) : (
        <EmptyState
          title="No namespaces yet"
          description={me?.isAdmin ? 'Create the first namespace to get started.' : 'You have not been granted access to any namespace. Ask a namespace owner to add you.'}
          action={me?.isAdmin ? <CreateNamespaceDialog /> : undefined}
        />
      )}
    </>
  )
}
