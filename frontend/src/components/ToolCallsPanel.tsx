import { Wrench } from 'lucide-react'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'

interface ToolCallsPanelProps {
  toolCalls: string[]
}

/** Parse "name({json})" into a readable summary without dumping raw JSON to users. */
function summarize(call: string): { name: string; args: string } {
  const match = call.match(/^([a-zA-Z_][\w]*)\((.*)\)$/s)
  if (!match) return { name: call, args: '' }
  const [, name, json] = match
  try {
    const parsed = JSON.parse(json) as Record<string, unknown>
    const args = Object.entries(parsed)
      .map(([k, v]) => `${k}=${typeof v === 'string' ? JSON.stringify(v) : String(v)}`)
      .join(', ')
    return { name, args }
  } catch {
    return { name, args: json }
  }
}

export function ToolCallsPanel({ toolCalls }: ToolCallsPanelProps) {
  if (toolCalls.length === 0) return null

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Wrench className="h-4 w-4 text-primary" />
          Tool calls
        </CardTitle>
        <CardDescription>Actions the assistant took to answer the last message.</CardDescription>
      </CardHeader>
      <CardContent>
        <ul className="space-y-2">
          {toolCalls.map((call, index) => {
            const { name, args } = summarize(call)
            return (
              <li
                key={`${name}-${index}`}
                className="rounded-lg border border-border bg-surface/60 px-3 py-2 space-y-1.5 min-w-0"
              >
                <Badge tone="success" className="font-mono">
                  {name}
                </Badge>
                {args && (
                  <pre className="text-[11px] text-muted font-mono leading-snug whitespace-pre-wrap break-all m-0">
                    {args}
                  </pre>
                )}
              </li>
            )
          })}
        </ul>
      </CardContent>
    </Card>
  )
}
