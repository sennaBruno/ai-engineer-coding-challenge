import { BookOpen } from 'lucide-react'
import type { Citation } from '@/types/chat'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/Card'

interface CitationsPanelProps {
  citations: Citation[]
}

export function CitationsPanel({ citations }: CitationsPanelProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <BookOpen className="h-4 w-4 text-primary" />
          Citations
        </CardTitle>
        <CardDescription>
          Passages the assistant retrieved from the SOP to ground the most recent answer.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {citations.length === 0 ? (
          <p className="text-sm text-muted">
            No citations yet. Ask a procedural question and retrieved SOP passages will appear here.
          </p>
        ) : (
          <ul className="space-y-3">
            {citations.map((citation, index) => (
              <li
                key={`${citation.source}-${index}`}
                className="rounded-lg border border-border bg-surface/60 px-3 py-2.5 text-sm"
              >
                <p className="text-xs font-semibold text-foreground mb-1 flex items-center gap-2">
                  <span>{citation.source}</span>
                  {citation.startLine ? (
                    <span className="font-normal text-muted">
                      L{citation.startLine}
                      {citation.endLine && citation.endLine !== citation.startLine
                        ? `–${citation.endLine}`
                        : ''}
                    </span>
                  ) : null}
                </p>
                <p className="text-xs text-muted leading-relaxed">{citation.snippet}</p>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
