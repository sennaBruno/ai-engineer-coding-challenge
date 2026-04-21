import type {
  ChatRequest,
  ChatResponse,
  HealthResponse,
  IngestRequest,
  IngestResponse,
} from '../types/chat'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5181').replace(/\/$/, '')

async function request<TResponse>(path: string, init?: RequestInit): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
    ...init,
  })

  if (!response.ok) {
    let errorMessage = `Request failed with status ${response.status}`

    try {
      const errorBody = (await response.json()) as {
        error?: string
        assistantMessage?: string
      }
      // Chat errors (502) carry an assistantMessage plus Status="error"; surface
      // that user-facing message directly. Other endpoints use { error: "..." }.
      if (errorBody.assistantMessage) {
        errorMessage = errorBody.assistantMessage
      } else if (errorBody.error) {
        errorMessage = errorBody.error
      }
    } catch {
      errorMessage = `${errorMessage}. The backend may be offline or returned non-JSON content.`
    }

    throw new Error(errorMessage)
  }

  return (await response.json()) as TResponse
}

export const apiClient = {
  getHealth(): Promise<HealthResponse> {
    return request<HealthResponse>('/api/health')
  },
  ingest(payload: IngestRequest): Promise<IngestResponse> {
    return request<IngestResponse>('/api/ingest', {
      method: 'POST',
      body: JSON.stringify(payload),
    })
  },
  chat(payload: ChatRequest): Promise<ChatResponse> {
    return request<ChatResponse>('/api/chat', {
      method: 'POST',
      body: JSON.stringify(payload),
    })
  },
}