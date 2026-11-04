import type { ReactNode } from 'react'
import { AlertTriangle, Inbox } from 'lucide-react'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { cn } from '@/lib/utils'
import { errorMessages } from '@/lib/errors'

export function PageHeader({
  title,
  description,
  actions,
  children,
}: {
  title: ReactNode
  description?: ReactNode
  actions?: ReactNode
  children?: ReactNode
}) {
  return (
    <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0 space-y-1">
        <h1 className="truncate text-2xl font-semibold tracking-tight">{title}</h1>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
        {children}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}

export function EmptyState({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed p-10 text-center">
      <Inbox className="size-8 text-muted-foreground" />
      <p className="font-medium">{title}</p>
      {description && <p className="max-w-md text-sm text-muted-foreground">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}

export function ErrorState({ error, title = 'Something went wrong' }: { error: unknown; title?: string }) {
  const { title: t, lines } = errorMessages(error)
  return (
    <Alert variant="destructive">
      <AlertTriangle />
      <AlertTitle>{title}</AlertTitle>
      <AlertDescription>
        {t}
        {lines.map((l, i) => (
          <div key={i}>{l}</div>
        ))}
      </AlertDescription>
    </Alert>
  )
}

export function TableSkeleton({ rows = 5, cols = 5 }: { rows?: number; cols?: number }) {
  return (
    <div className="space-y-2" aria-label="loading">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex gap-3">
          {Array.from({ length: cols }).map((__, j) => (
            <Skeleton key={j} className="h-9 flex-1" />
          ))}
        </div>
      ))}
    </div>
  )
}

export interface Option {
  value: string
  label?: ReactNode
}
export function SimpleSelect({
  value,
  onChange,
  options,
  placeholder,
  className,
  disabled,
  id,
  size,
  'aria-label': ariaLabel,
}: {
  value: string
  onChange: (v: string) => void
  options: Option[]
  placeholder?: string
  className?: string
  disabled?: boolean
  id?: string
  size?: 'sm' | 'default'
  'aria-label'?: string
}) {
  return (
    <Select value={value} onValueChange={(v) => v !== null && onChange(v as string)} disabled={disabled}>
      <SelectTrigger id={id} size={size} className={cn('w-full', className)} aria-label={ariaLabel}>
        <SelectValue placeholder={placeholder}>
          {(v: string | null) => options.find((o) => o.value === v)?.label ?? v ?? placeholder}
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        {options.map((o) => (
          <SelectItem key={o.value} value={o.value}>
            {o.label ?? o.value}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export function Field({ label, htmlFor, hint, children }: { label: string; htmlFor?: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={htmlFor} className="text-sm font-medium leading-none">
        {label}
      </label>
      {children}
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  )
}
