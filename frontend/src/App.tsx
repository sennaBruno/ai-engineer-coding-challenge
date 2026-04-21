import { ChatPage } from '@/pages/ChatPage'
import { ErrorBoundary } from '@/components/ErrorBoundary'

function App() {
  return (
    <ErrorBoundary>
      <ChatPage />
    </ErrorBoundary>
  )
}

export default App
