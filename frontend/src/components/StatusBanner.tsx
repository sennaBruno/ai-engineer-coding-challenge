import { AlertTriangle, CheckCircle2, CircleAlert, Info } from 'lucide-react'
import type { StatusMessage } from '@/types/chat'
import { cn } from '@/lib/cn'

interface StatusBannerProps {
  status: StatusMessage
}

const toneStyles = {
  info: 'bg-user/50 border-border text-foreground',
  success: 'bg-assistant/70 border-primary/20 text-foreground',
  warning: 'bg-amber-50 border-amber-200 text-amber-900',
  error: 'bg-red-50 border-red-200 text-red-900',
} as const

const toneIcons = {
  info: Info,
  success: CheckCircle2,
  warning: AlertTriangle,
  error: CircleAlert,
} as const

export function StatusBanner({ status }: StatusBannerProps) {
  const Icon = toneIcons[status.tone]
  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(
        'mx-5 mt-4 flex items-start gap-2.5 rounded-lg border px-3.5 py-2.5 text-sm',
        toneStyles[status.tone]
      )}
    >
      <Icon className="h-4 w-4 mt-0.5 flex-shrink-0" />
      <span className="leading-snug">{status.message}</span>
    </div>
  )
}
