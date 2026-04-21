import type { HTMLAttributes } from 'react'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/cn'

const badgeStyles = cva(
  'inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-medium border',
  {
    variants: {
      tone: {
        neutral: 'bg-surface text-muted border-border',
        success: 'bg-assistant text-primary border-primary/20',
        info: 'bg-user text-foreground border-border',
        warn: 'bg-amber-50 text-amber-900 border-amber-200',
        error: 'bg-red-50 text-red-900 border-red-200',
      },
    },
    defaultVariants: { tone: 'neutral' },
  }
)

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement>, VariantProps<typeof badgeStyles> {}

export function Badge({ className, tone, ...props }: BadgeProps) {
  return <span className={cn(badgeStyles({ tone }), className)} {...props} />
}
