import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, Settings2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Badge } from '@/components/ui/badge'
import { ErrorState, PageHeader, SimpleSelect } from '@/components/common/misc'
import { TypeBadge } from '@/components/common/badges'
import { FlagEditor } from '@/features/flags/FlagEditor'
import { CompareEnvs } from '@/features/flags/CompareEnvs'
import { useFlag } from '@/api/flags'
import { useNamespace } from '@/api/namespaces'
import { usePolicies } from '@/api/settings'
import { effectiveRole, roleAtLeast, useEnvironments, useMe } from '@/api/me'
import { defaultFlagForm, formToFlag } from '@/lib/flagForm'
import type { FlagDefinition, FlagType } from '@/api/types'
import { stableStringify } from '@/lib/diff'

function Configure({
  type,
  flagKey,
  env,
  configs,
  envKeys,
  canEdit,
  onConfigure,
}: {
  type: FlagType
  flagKey: string
  env: string
  configs: Record<string, FlagDefinition | null>
  envKeys: string[]
  canEdit: boolean
  onConfigure: (f: FlagDefinition) => void
}) {
  const sources = envKeys.filter((k) => k !== env && configs[k])
  const [from, setFrom] = useState(sources[0] ?? 'defaults')
  const create = () => {
    if (from !== 'defaults' && configs[from]) onConfigure(structuredClone(configs[from]!))
    else onConfigure(formToFlag(type, flagKey, defaultFlagForm(type)).flag ?? { type, enabled: true, variants: {}, defaultVariant: '', rules: [] })
  }
  return (
    <Card>
      <CardHeader>
        <CardTitle>Not configured in {env}</CardTitle>
        <CardDescription>This flag has no configuration here, so it is not part of the {env} snapshot.</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-wrap items-end gap-3">
        <div className="w-64 space-y-1.5">
          <label className="text-sm font-medium">Start from</label>
          <SimpleSelect
            aria-label="Copy from"
            value={from}
            onChange={setFrom}
            options={[...sources.map((s) => ({ value: s, label: `Copy from ${s}` })), { value: 'defaults', label: `Defaults for ${type}` }]}
          />
        </div>
        <Button disabled={!canEdit} onClick={create}>
          <Settings2 /> Configure in this environment
        </Button>
        {!canEdit && <span className="text-xs text-muted-foreground">Requires the editor role.</span>}
      </CardContent>
    </Card>
  )
}

export default function FlagPage() {
  const { ns = '', key = '' } = useParams()
  const [sp, setSp] = useSearchParams()
  const { data: flag, isLoading, error } = useFlag(ns, key)
  const { data: envs } = useEnvironments()
  const { data: namespace } = useNamespace(ns)
  const { data: me } = useMe()
  const { data: policies } = usePolicies(ns)
  const [drafts, setDrafts] = useState<Record<string, FlagDefinition>>({})

  if (error) return <ErrorState error={error} title="Could not load flag" />
  if (isLoading || !flag || !envs) return <Skeleton className="h-96" />

  const role = effectiveRole(me, ns, namespace?.myRole)
  const canEdit = roleAtLeast(role, 'editor')
  const env = sp.get('env') && (sp.get('env') === 'compare' || envs.some((e) => e.key === sp.get('env'))) ? sp.get('env')! : envs[0]?.key
  const setEnv = (e: string) => setSp({ env: e }, { replace: true })

  return (
    <>
      <Button variant="ghost" size="sm" className="mb-2 -ml-2" render={<Link to={`/namespaces/${ns}?env=${env === 'compare' ? '' : env}`} />}>
        <ArrowLeft /> {namespace?.name ?? ns}
      </Button>
      <PageHeader
        title={
          <span className="flex flex-wrap items-center gap-3">
            <span className="font-mono">{flag.key}</span> <TypeBadge type={flag.type} />
          </span>
        }
        description={flag.name + (flag.description ? ` — ${flag.description}` : '')}
      />
      <Tabs value={env} onValueChange={(v) => setEnv(v as string)}>
        <TabsList>
          {envs.map((e) => (
            <TabsTrigger key={e.key} value={e.key}>
              {e.name || e.key}
              {flag.configs[e.key] ? null : <Badge variant="outline" className="ml-1.5 px-1 text-[10px]">unset</Badge>}
            </TabsTrigger>
          ))}
          <TabsTrigger value="compare">Compare</TabsTrigger>
        </TabsList>
        {envs.map((e) => {
          const server = flag.configs[e.key]
          const initial = server ?? drafts[e.key]
          return (
            <TabsContent key={e.key} value={e.key} className="mt-4">
              {initial ? (
                <FlagEditor
                  key={`${e.key}:${server ? 'live' : 'draft'}:${stableStringify(initial)}`}
                  ns={ns}
                  env={e.key}
                  flagKey={flag.key}
                  type={flag.type}
                  initial={initial}
                  canEdit={canEdit}
                  isNew={!server}
                  policy={policies?.find((p) => p.environment === e.key)}
                />
              ) : (
                <Configure
                  type={flag.type}
                  flagKey={flag.key}
                  env={e.key}
                  configs={flag.configs}
                  envKeys={envs.map((x) => x.key)}
                  canEdit={canEdit}
                  onConfigure={(f) => setDrafts((d) => ({ ...d, [e.key]: f }))}
                />
              )}
            </TabsContent>
          )
        })}
        <TabsContent value="compare" className="mt-4">
          <CompareEnvs envs={envs} configs={flag.configs} />
        </TabsContent>
      </Tabs>
    </>
  )
}
