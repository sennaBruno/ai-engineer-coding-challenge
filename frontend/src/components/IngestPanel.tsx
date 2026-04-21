import { Upload } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'

interface IngestPanelProps {
  sourcePath: string
  onSourcePathChange: (value: string) => void
  onIngest: (forceReingest: boolean) => void
  isBusy: boolean
  chunksIngested?: number
  forceReingest: boolean
  onForceReingestChange: (value: boolean) => void
}

export function IngestPanel({
  sourcePath,
  onSourcePathChange,
  onIngest,
  isBusy,
  chunksIngested,
  forceReingest,
  onForceReingestChange,
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

        <label className="flex items-center gap-2 text-xs text-muted cursor-pointer select-none">
          <input
            type="checkbox"
            className="h-3.5 w-3.5 accent-primary"
            checked={forceReingest}
            onChange={(event) => onForceReingestChange(event.target.checked)}
            disabled={isBusy}
          />
          Force re-ingest (rebuilds the vector store even if records already exist)
        </label>

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
            onClick={() => onIngest(forceReingest)}
            disabled={isBusy || sourcePath.trim().length === 0}
          >
            {isBusy ? 'Ingesting…' : forceReingest ? 'Re-ingest' : 'Run ingest'}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
