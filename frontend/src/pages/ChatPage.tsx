import { useEffect, useState } from 'react'
import { ShoppingBasket } from 'lucide-react'
import { apiClient } from '@/services/apiClient'
import type { ChatMessage, Citation, StatusMessage } from '@/types/chat'
import { ChatComposer } from '@/components/ChatComposer'
import { ChatTranscript } from '@/components/ChatTranscript'
import { CitationsPanel } from '@/components/CitationsPanel'
import { IngestPanel } from '@/components/IngestPanel'
import { StatusBanner } from '@/components/StatusBanner'
import { ToolCallsPanel } from '@/components/ToolCallsPanel'

const DEFAULT_SOURCE_PATH = '../../../knowledge-base/Grocery_Store_SOP.md'

function createMessage(role: ChatMessage['role'], content: string): ChatMessage {
  return {
    id: window.crypto.randomUUID(),
    role,
    content,
    timestamp: new Date().toISOString(),
  }
}

const STARTER_QUESTIONS = [
  'What are the opening checklist steps for the manager on duty?',
  'How much can a cashier refund without a manager?',
  'Where is the milk?',
  'What are the food safety temperature rules for the deli?',
]

export function ChatPage() {
  const [conversationId] = useState(() => window.crypto.randomUUID())
  const [draft, setDraft] = useState('')
  const [sourcePath, setSourcePath] = useState(DEFAULT_SOURCE_PATH)
  const [forceReingest, setForceReingest] = useState(false)
  const [isSending, setIsSending] = useState(false)
  const [isIngesting, setIsIngesting] = useState(false)
  const [citations, setCitations] = useState<Citation[]>([])
  const [toolCalls, setToolCalls] = useState<string[]>([])
  const [chunksIngested, setChunksIngested] = useState<number>(0)
  const [status, setStatus] = useState<StatusMessage>({
    tone: 'info',
    message: 'Checking backend health…',
  })
  const [messages, setMessages] = useState<ChatMessage[]>([
    createMessage(
      'assistant',
      'Hi! I\'m your SOP assistant. Ingest the SOP document using the panel on the right, then ask me anything about store procedures, safety rules, or where to find products.',
    ),
  ])

  useEffect(() => {
    let isCancelled = false
    async function loadHealth() {
      try {
        const health = await apiClient.getHealth()
        if (!isCancelled) {
          setStatus({
            tone: 'success',
            message: `Backend online — ${health.service}`,
          })
        }
      } catch (error) {
        if (!isCancelled) {
          setStatus({
            tone: 'warning',
            message:
              error instanceof Error
                ? `Backend health check failed: ${error.message}`
                : 'Backend health check failed.',
          })
        }
      }
    }
    void loadHealth()
    return () => {
      isCancelled = true
    }
  }, [])

  async function handleIngest(shouldForce: boolean) {
    setIsIngesting(true)
    setStatus({
      tone: 'info',
      message: shouldForce
        ? 'Re-ingesting SOP — rebuilding the vector store…'
        : 'Ingesting SOP — chunking, embedding, persisting…',
    })

    try {
      const response = await apiClient.ingest({ sourcePath, forceReingest: shouldForce })
      setChunksIngested(response.recordsPersisted)
      setStatus({
        tone: response.accepted ? 'success' : 'warning',
        message: response.message,
      })
      // One-shot: uncheck after a successful forced rebuild so a second click
      // doesn't silently re-embed on every press.
      if (shouldForce && response.accepted) {
        setForceReingest(false)
      }
    } catch (error) {
      setStatus({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Ingest request failed.',
      })
    } finally {
      setIsIngesting(false)
    }
  }

  async function handleSend(override?: string) {
    const candidate = (override ?? draft).trim()
    if (!candidate) return
    if (isSending) return

    const userMessage = createMessage('user', candidate)
    // Build the snapshot synchronously from the closure value. React does NOT
    // run setState updaters synchronously in event handlers, so reading from
    // inside the updater into a local would leave the outer variable empty by
    // the time we call fetch. Double-submit is guarded by the `isSending` check
    // at the top of this function, so a plain spread is safe here.
    const snapshot = [...messages, userMessage]
    setMessages(snapshot)
    setDraft('')
    setIsSending(true)
    setStatus({ tone: 'info', message: 'Thinking…' })

    try {
      const response = await apiClient.chat({
        conversationId,
        useTools: true,
        messages: snapshot.map((message) => ({
          role: message.role,
          content: message.content,
          timestampUtc: message.timestamp,
        })),
      })

      setMessages((current) => [...current, createMessage('assistant', response.assistantMessage)])
      setCitations(response.citations)
      setToolCalls(response.toolCalls)
      setStatus({
        tone: response.status === 'ok' ? 'success' : 'warning',
        message: `Response received · status: ${response.status}`,
      })
    } catch (error) {
      setMessages((current) => [
        ...current,
        createMessage(
          'assistant',
          'The chat request failed. Make sure the backend is running and the OpenAI key is configured.',
        ),
      ])
      setStatus({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Chat request failed.',
      })
    } finally {
      setIsSending(false)
    }
  }

  const showStarters = messages.length <= 1 && !isSending

  return (
    <main className="min-h-screen lg:h-screen grid gap-4 p-4 lg:p-6 grid-cols-1 lg:grid-cols-[minmax(0,1fr)_22rem] max-w-[1400px] mx-auto lg:overflow-hidden">
      <section className="flex flex-col rounded-2xl border border-border bg-card/80 backdrop-blur-sm shadow-[0_30px_80px_-30px_oklch(30%_0.02_260_/_0.18)] overflow-hidden min-h-[75vh] lg:min-h-0">
        <header className="px-6 pt-6 pb-4 border-b border-border bg-gradient-to-br from-accent/40 to-surface">
          <div className="flex items-center gap-3">
            <div className="h-10 w-10 rounded-xl bg-primary/10 flex items-center justify-center">
              <ShoppingBasket className="h-5 w-5 text-primary" />
            </div>
            <div>
              <h1 className="text-lg font-semibold tracking-tight">Grocery SOP Assistant</h1>
              <p className="text-xs text-muted">
                Grounded chatbot for employees · RAG + tool-calling over the store SOP
              </p>
            </div>
          </div>
        </header>

        <StatusBanner status={status} />

        <ChatTranscript messages={messages} isStreaming={isSending} />

        {showStarters && (
          <div
            role="group"
            aria-label="Suggested questions"
            className="px-5 pb-3 flex flex-wrap gap-2"
          >
            {STARTER_QUESTIONS.map((question) => (
              <button
                key={question}
                type="button"
                disabled={isSending}
                onClick={() => void handleSend(question)}
                className="text-xs rounded-full border border-border bg-surface/70 px-3 py-1.5 text-muted hover:text-foreground hover:bg-accent/60 transition disabled:opacity-50 disabled:pointer-events-none"
              >
                {question}
              </button>
            ))}
          </div>
        )}

        <ChatComposer
          value={draft}
          onChange={setDraft}
          onSubmit={() => void handleSend()}
          isBusy={isSending}
        />
      </section>

      <aside className="flex flex-col gap-4 lg:overflow-y-auto lg:pr-1 lg:min-h-0">
        <IngestPanel
          sourcePath={sourcePath}
          onSourcePathChange={setSourcePath}
          onIngest={handleIngest}
          isBusy={isIngesting}
          chunksIngested={chunksIngested}
          forceReingest={forceReingest}
          onForceReingestChange={setForceReingest}
        />
        <ToolCallsPanel toolCalls={toolCalls} />
        <CitationsPanel citations={citations} />
      </aside>
    </main>
  )
}
