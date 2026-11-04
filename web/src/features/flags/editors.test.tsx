import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RulesEditor } from './RulesEditor'
import { VariantsEditor } from './VariantsEditor'
import { newRule, type RuleRow, type VariantRow } from '@/lib/flagForm'

function RulesHarness({ initial }: { initial: RuleRow[] }) {
  const [rules, setRules] = useState(initial)
  return <RulesEditor rules={rules} variantNames={['on', 'off']} onChange={setRules} />
}
function VariantsHarness({ type, initial }: { type: 'integer' | 'object'; initial: VariantRow[] }) {
  const [rows, setRows] = useState(initial)
  return <VariantsEditor type={type} rows={rows} onChange={setRows} />
}

describe('RulesEditor', () => {
  it('warns when rollout weights do not sum to 100 and clears when fixed', async () => {
    const user = userEvent.setup()
    const rule = { ...newRule(['on', 'off']), mode: 'rollout' as const, rollout: [{ id: 'a', variant: 'on', weight: '30' }, { id: 'b', variant: 'off', weight: '30' }] }
    render(<RulesHarness initial={[rule]} />)
    expect(screen.getByTestId('rollout-sum')).toHaveTextContent('Weights must sum to 100 (currently 60)')
    const w = screen.getByLabelText('Rollout 2 weight')
    await user.clear(w)
    await user.type(w, '70')
    expect(screen.getByTestId('rollout-sum')).toHaveTextContent('Total 100%')
  })

  it('switches to rollout mode with an initial 100% split', async () => {
    const user = userEvent.setup()
    render(<RulesHarness initial={[newRule(['on', 'off'])]} />)
    await user.click(screen.getByRole('button', { name: 'Percentage rollout' }))
    expect(screen.getByLabelText('Rollout 1 weight')).toHaveValue(100)
    expect(screen.getByTestId('rollout-sum')).toHaveTextContent('Total 100%')
  })

  it('reorders and deletes rules', async () => {
    const user = userEvent.setup()
    const r1 = { ...newRule(['on']), name: 'first' }
    const r2 = { ...newRule(['on']), name: 'second' }
    render(<RulesHarness initial={[r1, r2]} />)
    const names = () => screen.getAllByTestId('rule-card').map((c) => (within(c).getByPlaceholderText('Rule name (optional)') as HTMLInputElement).value)
    expect(names()).toEqual(['first', 'second'])
    await user.click(screen.getByLabelText('Move rule 2 up'))
    expect(names()).toEqual(['second', 'first'])
    await user.click(screen.getByLabelText('Delete rule 1'))
    expect(names()).toEqual(['first'])
  })

  it('edits conditions and shows value tags', async () => {
    const user = userEvent.setup()
    render(<RulesHarness initial={[newRule(['on'])]} />)
    await user.click(screen.getByRole('button', { name: /Add condition/ }))
    await user.type(screen.getByLabelText('Rule 1 condition 1 attribute'), 'tenant')
    await user.type(screen.getByLabelText('Rule 1 condition 1 values'), 'acme, globex')
    const tags = within(screen.getByTestId('condition-row')).getAllByText(/acme|globex/)
    expect(tags.length).toBeGreaterThanOrEqual(2)
  })
})

describe('VariantsEditor', () => {
  it('validates integer input', async () => {
    const user = userEvent.setup()
    render(<VariantsHarness type="integer" initial={[{ id: '1', name: 'small', raw: '10' }]} />)
    const input = screen.getByLabelText('Variant 1 value')
    expect(screen.queryByRole('alert')).toBeNull()
    await user.clear(input)
    await user.type(input, '1.5')
    expect(screen.getByRole('alert')).toHaveTextContent('Must be an integer')
  })

  it('validates JSON for object variants', async () => {
    const user = userEvent.setup()
    render(<VariantsHarness type="object" initial={[{ id: '1', name: 'cfg', raw: '{}' }]} />)
    const input = screen.getByLabelText('Variant 1 value')
    await user.clear(input)
    await user.type(input, 'not json')
    expect(screen.getByRole('alert')).toHaveTextContent(/Invalid JSON/)
  })

  it('adds and removes variants', async () => {
    const user = userEvent.setup()
    render(<VariantsHarness type="integer" initial={[{ id: '1', name: 'a', raw: '1' }]} />)
    await user.click(screen.getByRole('button', { name: /Add variant/ }))
    expect(screen.getAllByTestId('variant-row')).toHaveLength(2)
    await user.click(screen.getByLabelText('Remove variant variant-2'))
    expect(screen.getAllByTestId('variant-row')).toHaveLength(1)
  })
})
