import { toast } from 'sonner'
import { ApiError } from '@/api/client'

export function errorMessages(e: unknown): { title: string; lines: string[] } {
  if (e instanceof ApiError) {
    const lines = [...(e.problem?.detail && e.problem.detail !== e.message ? [e.problem.detail] : []), ...e.messages]
    const title = e.status === 403 ? (e.message || 'You do not have permission to do this') : e.message
    return { title, lines }
  }
  return { title: e instanceof Error ? e.message : 'Unexpected error', lines: [] }
}

export function toastError(e: unknown, prefix?: string) {
  const { title, lines } = errorMessages(e)
  toast.error(prefix ? `${prefix}: ${title}` : title, {
    description:
      lines.length > 0 ? (
        <ul className="mt-1 list-disc space-y-0.5 pl-4">
          {lines.map((l, i) => (
            <li key={i}>{l}</li>
          ))}
        </ul>
      ) : undefined,
    duration: 8000,
  })
}
