import { useState } from 'react'
import { SimpleSelect } from '@/components/common/misc'
import { DiffView } from '@/components/common/DiffView'
import type { Environment, FlagDefinition } from '@/api/types'

export function CompareEnvs({ envs, configs }: { envs: Environment[]; configs: Record<string, FlagDefinition | null> }) {
  const [a, setA] = useState(envs[0]?.key ?? '')
  const [b, setB] = useState(envs[1]?.key ?? envs[0]?.key ?? '')
  const opts = envs.map((e) => ({ value: e.key, label: e.name || e.key }))
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3">
        <SimpleSelect aria-label="Compare left environment" value={a} onChange={setA} options={opts} />
        <SimpleSelect aria-label="Compare right environment" value={b} onChange={setB} options={opts} />
      </div>
      <DiffView before={configs[a] ?? null} after={configs[b] ?? null} beforeLabel={a} afterLabel={b} />
      {(!configs[a] || !configs[b]) && (
        <p className="text-xs text-muted-foreground">An environment without configuration is shown as empty.</p>
      )}
    </div>
  )
}
