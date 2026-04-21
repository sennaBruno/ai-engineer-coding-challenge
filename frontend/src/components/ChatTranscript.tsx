import { useEffect, useRef } from 'react'
import { Bot, User } from 'lucide-react'
import type { ChatMessage } from '@/types/chat'
import { cn } from '@/lib/cn'
import { MessageBody } from '@/components/MessageBody'

interface ChatTranscriptProps {
  messages: ChatMessage[]
  isStreaming?: boolean
}

function formatTimestamp(timestamp: string) {
  return new Date(timestamp).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
}

export function ChatTranscript({ messages, isStreaming }: ChatTranscriptProps) {
  const scrollRef = useRef<HTMLElement | null>(null)

  useEffect(() => {
    // Scroll the transcript's own container, NOT the element into view — the latter
    // escapes the flex-1 overflow container and scrolls the whole page under
    // viewport-contained layouts (lg:h-screen + overflow-hidden on <main>).
    const el = scrollRef.current
    if (el) {
      el.scrollTop = el.scrollHeight
    }
  }, [messages, isStreaming])

  // Reverse-scan the array in place instead of [...messages].reverse().find —
  // one pass, zero allocations. Matters less for correctness than for reading
  // the intent directly.
  let lastAssistant: ChatMessage | undefined
  for (let i = messages.length - 1; i >= 0; i--) {
    if (messages[i].role === 'assistant') {
      lastAssistant = messages[i]
      break
    }
  }

  return (
    <section
      ref={scrollRef}
      className="flex-1 overflow-y-auto px-5 py-6 space-y-4 scroll-smooth"
      aria-label="Chat transcript"
    >
      {messages.map((message) => {
        const isAssistant = message.role === 'assistant'
        const isNewestAssistant = isAssistant && message.id === lastAssistant?.id
        return (
          <article
            key={message.id}
            aria-label={`${isAssistant ? 'Assistant' : 'You'} at ${formatTimestamp(message.timestamp)}`}
            className={cn(
              'flex gap-3 max-w-[min(44rem,95%)]',
              isAssistant ? 'self-start mr-auto' : 'self-end ml-auto flex-row-reverse'
            )}
          >
            <div
              aria-hidden="true"
              className={cn(
                'flex-shrink-0 h-8 w-8 rounded-full flex items-center justify-center border border-border',
                isAssistant ? 'bg-assistant' : 'bg-user'
              )}
            >
              {isAssistant ? (
                <Bot className="h-4 w-4 text-primary" />
              ) : (
                <User className="h-4 w-4 text-foreground" />
              )}
            </div>
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2 text-xs text-muted mb-1">
                <span className="font-semibold text-foreground">
                  {isAssistant ? 'Assistant' : 'You'}
                </span>
                <span>{formatTimestamp(message.timestamp)}</span>
              </div>
              <div
                // Only the newest assistant message is a live region. This prevents
                // screen readers from re-announcing the entire history whenever the
                // user sends a new message (prior aria-live="polite" on the scroll
                // container did exactly that).
                aria-live={isNewestAssistant ? 'polite' : undefined}
                aria-atomic={isNewestAssistant ? 'true' : undefined}
                className={cn(
                  'rounded-xl border border-border px-4 py-3',
                  isAssistant ? 'bg-assistant/70' : 'bg-user/70'
                )}
              >
                <MessageBody content={message.content} asMarkdown={isAssistant} />
              </div>
            </div>
          </article>
        )
      })}

      {isStreaming && (
        <article
          className="flex gap-3 max-w-[min(44rem,95%)] self-start mr-auto"
          role="status"
          aria-label="Assistant is thinking"
        >
          <div
            aria-hidden="true"
            className="flex-shrink-0 h-8 w-8 rounded-full flex items-center justify-center border border-border bg-assistant"
          >
            <Bot className="h-4 w-4 text-primary" />
          </div>
          <div
            aria-hidden="true"
            className="rounded-xl border border-border bg-assistant/70 px-4 py-3 text-sm inline-flex items-center gap-1.5 motion-safe:[&>span]:animate-pulse"
          >
            <span className="h-1.5 w-1.5 rounded-full bg-primary" />
            <span className="h-1.5 w-1.5 rounded-full bg-primary [animation-delay:150ms]" />
            <span className="h-1.5 w-1.5 rounded-full bg-primary [animation-delay:300ms]" />
          </div>
        </article>
      )}
    </section>
  )
}
