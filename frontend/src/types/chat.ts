export type ChatRole = 'user' | 'assistant' | 'system' | 'tool'

export type StatusTone = 'info' | 'success' | 'warning' | 'error'

export interface ChatMessage {
  id: string
  role: ChatRole
  content: string
  timestamp: string
}

export interface Citation {
  source: string
  snippet: string
  startLine?: number
  endLine?: number
}

export interface ChatApiMessage {
  role: ChatRole
  content: string
  timestampUtc: string
}

export interface ChatRequest {
  conversationId: string
  messages: ChatApiMessage[]
  useTools: boolean
}

export interface ChatResponse {
  conversationId: string
  assistantMessage: string
  status: string
  isPlaceholder: boolean
  toolCalls: string[]
  citations: Citation[]
}

export interface IngestRequest {
  sourcePath: string
  forceReingest: boolean
}

export interface IngestResponse {
  accepted: boolean
  message: string
  sourcePath: string
  chunksCreated: number
  recordsPersisted: number
  vectorStorePath: string
  isPlaceholder: boolean
}

export interface HealthResponse {
  status: string
  service: string
  utcTime: string
  notes: string[]
}

export interface StatusMessage {
  tone: StatusTone
  message: string
}
