import { Upload } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'

interface IngestPanelProps {
  sourcePath: string
  onSourcePathChange: (value: string) => void
  onIngest: () => void
  isBusy: boolean
  chunksIngested?: number
}

export function IngestPanel({
  sourcePath,
  onSourcePathChange,
  onIngest,
  isBusy,
  chunksIngested,
}: IngestPanelProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Upload className="h-4 w-4 text-primary" />
          Ingest SOP
        </CardTitle>
        <CardDescription>
          Reads the source file, chunks by SOP section, embeds, and persists to
          the local JSON vector store.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="space-y-1.5">
          <label htmlFor="source-path" className="text-xs font-medium text-muted">
            Source document path
          </label>
          <Input
            id="source-path"
            value={sourcePath}
            onChange={(event) => onSourcePathChange(event.target.value)}
            disabled={isBusy}
            spellCheck={false}
          />
        </div>

        <div className="flex items-center justify-between gap-3">
          {typeof chunksIngested === 'number' && chunksIngested > 0 ? (
            <span className="text-xs text-muted">
              <span className="font-semibold text-foreground">{chunksIngested}</span> chunks ingested
            </span>
          ) : (
            <span className="text-xs text-muted">Not ingested yet</span>
          )}
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={onIngest}
            disabled={isBusy || sourcePath.trim().length === 0}
          >
            {isBusy ? 'Ingesting…' : 'Run ingest'}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
