using System.Text;
using Api.Models;

namespace Api.Services;

/// <summary>
/// Header-aware chunker for markdown SOP documents.
/// Splits on H2 (##) boundaries so each chunk corresponds to a logical SOP section,
/// which keeps retrieved context semantically coherent and makes citations meaningful.
/// Oversized sections are sub-split on paragraph boundaries to respect the embedding
/// context window and retrieval recall. Line ranges are tracked per emitted piece so
/// citations point at the exact paragraph span, not just the enclosing section.
/// </summary>
public sealed class MarkdownChunkingService : IChunkingService
{
    private const int MaxChunkChars = 2000;
    private const int MinChunkChars = 120;

    public Task<IReadOnlyList<TextChunk>> ChunkAsync(
        string sourceText,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        var lines = sourceText.Replace("\r\n", "\n").Split('\n');
        var sections = SplitIntoSections(lines);

        var chunks = new List<TextChunk>(sections.Count);
        var chunkIndex = 0;

        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var body = JoinParagraphs(section.Paragraphs).Trim();
            if (body.Length < MinChunkChars)
            {
                continue;
            }

            foreach (var piece in SplitLongSection(section))
            {
                chunks.Add(new TextChunk
                {
                    Source = sourceName,
                    Index = chunkIndex++,
                    Content = FormatChunkContent(section.Title, piece.Body),
                    Section = section.Title,
                    StartLine = piece.StartLine,
                    EndLine = piece.EndLine
                });
            }
        }

        return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
    }

    private static List<SectionBuffer> SplitIntoSections(string[] lines)
    {
        var sections = new List<SectionBuffer>();
        SectionBuffer? current = null;
        Paragraph? paragraph = null;

        void FlushParagraph()
        {
            if (paragraph is not null && paragraph.Builder.Length > 0)
            {
                paragraph.Text = paragraph.Builder.ToString().TrimEnd();
                if (!string.IsNullOrWhiteSpace(paragraph.Text))
                {
                    current!.Paragraphs.Add(paragraph);
                }
            }
            paragraph = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            if (line.StartsWith("## "))
            {
                FlushParagraph();
                if (current is not null)
                {
                    sections.Add(current);
                }

                current = new SectionBuffer
                {
                    Title = line[3..].Trim(),
                    StartLine = lineNumber
                };
                continue;
            }

            current ??= new SectionBuffer
            {
                // Preamble (title, intro) — emit as a synthetic "Document Header" section.
                Title = "Document Header",
                StartLine = lineNumber
            };

            // Blank line ends the current paragraph.
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            paragraph ??= new Paragraph { StartLine = lineNumber };
            paragraph.Builder.AppendLine(line);
            paragraph.EndLine = lineNumber;
        }

        FlushParagraph();
        if (current is not null)
        {
            sections.Add(current);
        }

        return sections;
    }

    private static IEnumerable<ChunkPiece> SplitLongSection(SectionBuffer section)
    {
        var paragraphs = section.Paragraphs;
        if (paragraphs.Count == 0)
        {
            yield break;
        }

        var combined = JoinParagraphs(paragraphs);
        if (combined.Length <= MaxChunkChars)
        {
            yield return new ChunkPiece(
                combined.Trim(),
                paragraphs[0].StartLine,
                paragraphs[^1].EndLine);
            yield break;
        }

        var buffer = new StringBuilder();
        var pieceStartLine = paragraphs[0].StartLine;
        var pieceEndLine = paragraphs[0].EndLine;

        foreach (var para in paragraphs)
        {
            if (buffer.Length > 0 && buffer.Length + para.Text.Length + 2 > MaxChunkChars)
            {
                yield return new ChunkPiece(buffer.ToString().Trim(), pieceStartLine, pieceEndLine);
                buffer.Clear();
                pieceStartLine = para.StartLine;
            }

            if (buffer.Length > 0)
            {
                buffer.Append("\n\n");
            }

            buffer.Append(para.Text);
            pieceEndLine = para.EndLine;
        }

        if (buffer.Length > 0)
        {
            yield return new ChunkPiece(buffer.ToString().Trim(), pieceStartLine, pieceEndLine);
        }
    }

    private static string JoinParagraphs(List<Paragraph> paragraphs)
    {
        return string.Join("\n\n", paragraphs.Select(p => p.Text));
    }

    private static string FormatChunkContent(string sectionTitle, string body)
    {
        // Prepend the section title so embeddings capture the topical header context,
        // which measurably improves retrieval recall for domain-specific questions.
        return $"## {sectionTitle}\n\n{body}";
    }

    private sealed class SectionBuffer
    {
        public string Title { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public List<Paragraph> Paragraphs { get; } = new();
    }

    private sealed class Paragraph
    {
        public StringBuilder Builder { get; } = new();
        public string Text { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
    }

    private readonly record struct ChunkPiece(string Body, int StartLine, int EndLine);
}
