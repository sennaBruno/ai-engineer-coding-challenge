import { useEffect, useRef } from 'react'
import { Bot, User } from 'lucide-react'
import type { ChatMessage } from '@/types/chat'
import { cn } from '@/lib/cn'

interface ChatTranscriptProps {
  messages: ChatMessage[]
  isStreaming?: boolean
}

function formatTimestamp(timestamp: string) {
  return new Date(timestamp).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
}

export function ChatTranscript({ messages, isStreaming }: ChatTranscriptProps) {
  const endOfMessagesRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    endOfMessagesRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [messages, isStreaming])

  return (
    <section
      className="flex-1 overflow-y-auto px-5 py-6 space-y-4 scroll-smooth"
      aria-label="Chat transcript"
      aria-live="polite"
    >
      {messages.map((message) => {
        const isAssistant = message.role === 'assistant'
        return (
          <article
            key={message.id}
            className={cn(
              'flex gap-3 max-w-[min(44rem,95%)]',
              isAssistant ? 'self-start mr-auto' : 'self-end ml-auto flex-row-reverse'
            )}
          >
            <div
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
                className={cn(
                  'rounded-xl border border-border px-4 py-3 text-sm whitespace-pre-wrap leading-relaxed',
                  isAssistant ? 'bg-assistant/70' : 'bg-user/70'
                )}
              >
                {message.content}
              </div>
            </div>
          </article>
        )
      })}

      {isStreaming && (
        <article className="flex gap-3 max-w-[min(44rem,95%)] self-start mr-auto">
          <div className="flex-shrink-0 h-8 w-8 rounded-full flex items-center justify-center border border-border bg-assistant">
            <Bot className="h-4 w-4 text-primary" />
          </div>
          <div className="rounded-xl border border-border bg-assistant/70 px-4 py-3 text-sm inline-flex items-center gap-1.5">
            <span className="h-1.5 w-1.5 rounded-full bg-primary animate-pulse" />
            <span className="h-1.5 w-1.5 rounded-full bg-primary animate-pulse [animation-delay:150ms]" />
            <span className="h-1.5 w-1.5 rounded-full bg-primary animate-pulse [animation-delay:300ms]" />
          </div>
        </article>
      )}
      <div ref={endOfMessagesRef} />
    </section>
  )
}
