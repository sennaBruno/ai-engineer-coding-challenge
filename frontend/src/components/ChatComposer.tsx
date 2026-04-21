import { type FormEvent, type KeyboardEvent } from 'react'
import { SendHorizonal } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'

interface ChatComposerProps {
  value: string
  onChange: (value: string) => void
  onSubmit: () => void
  isBusy: boolean
  disabled?: boolean
  disabledReason?: string
}

export function ChatComposer({
  value,
  onChange,
  onSubmit,
  isBusy,
  disabled,
  disabledReason,
}: ChatComposerProps) {
  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    onSubmit()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault()
      onSubmit()
    }
  }

  const isSubmitDisabled = isBusy || disabled || value.trim().length === 0

  return (
    <form
      onSubmit={handleSubmit}
      className="border-t border-border bg-card/60 px-5 py-4 space-y-2"
    >
      <label htmlFor="chat-input" className="sr-only">
        Ask about the grocery store SOP
      </label>
      <Textarea
        id="chat-input"
        rows={3}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        onKeyDown={handleKeyDown}
        placeholder={
          disabled && disabledReason
            ? disabledReason
            : 'Ask something — e.g. "What are the opening checklist steps for the manager on duty?"'
        }
        disabled={isBusy || disabled}
      />
      <div className="flex items-center justify-between gap-3">
        <span className="text-xs text-muted">
          <kbd className="rounded border border-border bg-surface px-1.5 py-0.5 font-mono text-[10px]">
            ⌘↵
          </kbd>{' '}
          to send
        </span>
        <Button type="submit" disabled={isSubmitDisabled} size="md">
          {isBusy ? 'Sending…' : 'Send'}
          <SendHorizonal className="h-4 w-4" />
        </Button>
      </div>
    </form>
  )
}
