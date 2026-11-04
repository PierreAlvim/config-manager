import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeft, Plus, Trash2 } from 'lucide-react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { EmptyState, ErrorState, PageHeader, SimpleSelect } from '@/components/common/misc'
import { RoleBadge } from '@/components/common/badges'
import { useNamespace } from '@/api/namespaces'
import { effectiveRole, useEnvironments, useMe } from '@/api/me'
import {
  useAddMember,
  useAddServiceAccess,
  useDeleteMember,
  useDeleteServiceAccess,
  useMembers,
  usePolicies,
  useServiceAccess,
  useUpdatePolicy,
} from '@/api/settings'
import { ROLES, type Policy, type Role } from '@/api/types'
import { toastError } from '@/lib/errors'

function PolicyRow({ ns, policy }: { ns: string; policy: Policy }) {
  const update = useUpdatePolicy(ns)
  const [approvals, setApprovals] = useState(String(policy.requiredApprovals))
  const parsed = Number.parseInt(approvals, 10)
  const valid = Number.isInteger(parsed) && parsed >= 1
  const save = async (p: Policy) => {
    try {
      await update.mutateAsync(p)
      toast.success(`Policy for ${p.environment} saved`)
    } catch (e) {
      toastError(e, 'Could not save policy')
    }
  }
  return (
    <TableRow>
      <TableCell className="font-medium">{policy.environment}</TableCell>
      <TableCell>
        <Switch
          aria-label={`Require change request in ${policy.environment}`}
          checked={policy.requireChangeRequest}
          onCheckedChange={(c) => save({ ...policy, requireChangeRequest: c })}
        />
      </TableCell>
      <TableCell>
        <div className="flex items-center gap-2">
          <Input
            aria-label={`Required approvals in ${policy.environment}`}
            type="number"
            min={1}
            className="w-20"
            value={approvals}
            aria-invalid={!valid}
            onChange={(e) => setApprovals(e.target.value)}
          />
          <Button size="sm" variant="outline" disabled={!valid || parsed === policy.requiredApprovals} onClick={() => save({ ...policy, requiredApprovals: parsed })}>
            Save
          </Button>
        </div>
      </TableCell>
    </TableRow>
  )
}

function PoliciesTab({ ns }: { ns: string }) {
  const { data, isLoading, error } = usePolicies(ns)
  return (
    <Card>
      <CardHeader>
        <CardTitle>Change policies</CardTitle>
        <CardDescription>Require review before changes are published to an environment.</CardDescription>
      </CardHeader>
      <CardContent>
        {error ? (
          <ErrorState error={error} />
        ) : isLoading || !data ? (
          <Skeleton className="h-32" />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Environment</TableHead>
                <TableHead>Require change request</TableHead>
                <TableHead>Required approvals</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((p) => (
                <PolicyRow key={p.environment + p.requiredApprovals + p.requireChangeRequest} ns={ns} policy={p} />
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}

function MembersTab({ ns }: { ns: string }) {
  const { data, isLoading, error } = useMembers(ns)
  const add = useAddMember(ns)
  const del = useDeleteMember(ns)
  const [subjectType, setType] = useState<'user' | 'group'>('user')
  const [subject, setSubject] = useState('')
  const [role, setRole] = useState<Role>('viewer')
  const submit = async () => {
    try {
      await add.mutateAsync({ subjectType, subject: subject.trim(), role })
      setSubject('')
      toast.success('Member added')
    } catch (e) {
      toastError(e, 'Could not add member')
    }
  }
  return (
    <Card>
      <CardHeader>
        <CardTitle>Members</CardTitle>
        <CardDescription>Users (preferred_username) or IdP groups and their role in this namespace.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-2 sm:grid-cols-[8rem_1fr_9rem_auto]">
          <SimpleSelect aria-label="Subject type" value={subjectType} onChange={(v) => setType(v as 'user' | 'group')} options={[{ value: 'user' }, { value: 'group' }]} />
          <Input aria-label="Subject" placeholder={subjectType === 'user' ? 'username' : 'group name'} value={subject} onChange={(e) => setSubject(e.target.value)} />
          <SimpleSelect aria-label="Role" value={role} onChange={(v) => setRole(v as Role)} options={ROLES.map((r) => ({ value: r }))} />
          <Button onClick={submit} disabled={!subject.trim() || add.isPending}>
            <Plus /> Add
          </Button>
        </div>
        {error ? (
          <ErrorState error={error} />
        ) : isLoading ? (
          <Skeleton className="h-24" />
        ) : !data || data.length === 0 ? (
          <EmptyState title="No members" />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Type</TableHead>
                <TableHead>Subject</TableHead>
                <TableHead>Role</TableHead>
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((m) => (
                <TableRow key={m.id}>
                  <TableCell>
                    <Badge variant="outline">{m.subjectType}</Badge>
                  </TableCell>
                  <TableCell>{m.subject}</TableCell>
                  <TableCell>
                    <RoleBadge role={m.role} />
                  </TableCell>
                  <TableCell>
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={`Remove ${m.subject}`}
                      onClick={async () => {
                        try {
                          await del.mutateAsync(m.id)
                          toast.success('Member removed')
                        } catch (e) {
                          toastError(e, 'Could not remove member')
                        }
                      }}
                    >
                      <Trash2 />
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}

function ServiceAccessTab({ ns }: { ns: string }) {
  const { data, isLoading, error } = useServiceAccess(ns)
  const { data: envs } = useEnvironments()
  const add = useAddServiceAccess(ns)
  const del = useDeleteServiceAccess(ns)
  const [clientId, setClientId] = useState('')
  const [environment, setEnvironment] = useState('*')
  const submit = async () => {
    try {
      await add.mutateAsync({ clientId: clientId.trim(), environment })
      setClientId('')
      toast.success('Service access granted')
    } catch (e) {
      toastError(e, 'Could not grant access')
    }
  }
  return (
    <Card>
      <CardHeader>
        <CardTitle>Service access</CardTitle>
        <CardDescription>OIDC clients (service accounts) allowed to read this namespace's snapshots from the Distribution API.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-2 sm:grid-cols-[1fr_11rem_auto]">
          <Input aria-label="Client ID" placeholder="client id, e.g. orders-service" value={clientId} onChange={(e) => setClientId(e.target.value)} />
          <SimpleSelect
            aria-label="Environment"
            value={environment}
            onChange={setEnvironment}
            options={[{ value: '*', label: '* (all environments)' }, ...(envs ?? []).map((e) => ({ value: e.key, label: e.name || e.key }))]}
          />
          <Button onClick={submit} disabled={!clientId.trim() || add.isPending}>
            <Plus /> Grant
          </Button>
        </div>
        {error ? (
          <ErrorState error={error} />
        ) : isLoading ? (
          <Skeleton className="h-24" />
        ) : !data || data.length === 0 ? (
          <EmptyState title="No service access grants" />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Client ID</TableHead>
                <TableHead>Environment</TableHead>
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((g) => (
                <TableRow key={g.id}>
                  <TableCell className="font-mono text-sm">{g.clientId}</TableCell>
                  <TableCell>
                    <Badge variant="secondary">{g.environment}</Badge>
                  </TableCell>
                  <TableCell>
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={`Revoke ${g.clientId}`}
                      onClick={async () => {
                        try {
                          await del.mutateAsync(g.id)
                          toast.success('Access revoked')
                        } catch (e) {
                          toastError(e, 'Could not revoke')
                        }
                      }}
                    >
                      <Trash2 />
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}

export default function SettingsPage() {
  const { ns = '' } = useParams()
  const { data: namespace, isLoading } = useNamespace(ns)
  const { data: me } = useMe()
  if (isLoading || !me) return <Skeleton className="h-64" />
  const role = effectiveRole(me, ns, namespace?.myRole)
  return (
    <>
      <Button variant="ghost" size="sm" className="mb-2 -ml-2" render={<Link to={`/namespaces/${ns}`} />}>
        <ArrowLeft /> {namespace?.name ?? ns}
      </Button>
      <PageHeader title="Namespace settings" description={`Access control and policies for ${ns}.`} />
      {role !== 'owner' ? (
        <EmptyState title="Owner access required" description="Only namespace owners and administrators can manage settings." />
      ) : (
        <Tabs defaultValue="policies">
          <TabsList>
            <TabsTrigger value="policies">Policies</TabsTrigger>
            <TabsTrigger value="members">Members</TabsTrigger>
            <TabsTrigger value="service">Service access</TabsTrigger>
          </TabsList>
          <TabsContent value="policies" className="mt-4">
            <PoliciesTab ns={ns} />
          </TabsContent>
          <TabsContent value="members" className="mt-4">
            <MembersTab ns={ns} />
          </TabsContent>
          <TabsContent value="service" className="mt-4">
            <ServiceAccessTab ns={ns} />
          </TabsContent>
        </Tabs>
      )}
    </>
  )
}
