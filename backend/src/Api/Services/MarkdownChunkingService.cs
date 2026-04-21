using System.Text;
using Api.Models;

namespace Api.Services;

/// <summary>
/// Header-aware chunker for markdown SOP documents.
/// Splits on H2 (##) boundaries so each chunk corresponds to a logical SOP section,
/// which keeps retrieved context semantically coherent and makes citations meaningful.
/// Oversized sections are sub-split on paragraph boundaries to respect the embedding
/// context window and retrieval recall.
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
            var body = section.BodyBuilder.ToString().Trim();
            if (body.Length < MinChunkChars)
            {
                continue;
            }

            foreach (var piece in SplitLongSection(body))
            {
                chunks.Add(new TextChunk
                {
                    Source = sourceName,
                    Index = chunkIndex++,
                    Content = FormatChunkContent(section.Title, piece),
                    Section = section.Title,
                    StartLine = section.StartLine,
                    EndLine = section.EndLine
                });
            }
        }

        return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
    }

    private static List<SectionBuffer> SplitIntoSections(string[] lines)
    {
        var sections = new List<SectionBuffer>();
        SectionBuffer? current = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            if (line.StartsWith("## "))
            {
                if (current is not null)
                {
                    current.EndLine = lineNumber - 1;
                    sections.Add(current);
                }

                current = new SectionBuffer
                {
                    Title = line[3..].Trim(),
                    StartLine = lineNumber,
                    EndLine = lineNumber
                };
                continue;
            }

            if (current is null)
            {
                // Preamble (title, intro) — emit as a synthetic "Document Header" section.
                current = new SectionBuffer
                {
                    Title = "Document Header",
                    StartLine = lineNumber,
                    EndLine = lineNumber
                };
            }

            current.BodyBuilder.AppendLine(line);
            current.EndLine = lineNumber;
        }

        if (current is not null)
        {
            sections.Add(current);
        }

        return sections;
    }

    private static IEnumerable<string> SplitLongSection(string body)
    {
        if (body.Length <= MaxChunkChars)
        {
            yield return body;
            yield break;
        }

        var paragraphs = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var buffer = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (buffer.Length > 0 && buffer.Length + paragraph.Length + 2 > MaxChunkChars)
            {
                yield return buffer.ToString().Trim();
                buffer.Clear();
            }

            if (buffer.Length > 0)
            {
                buffer.Append("\n\n");
            }

            buffer.Append(paragraph);
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString().Trim();
        }
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
        public StringBuilder BodyBuilder { get; } = new();
        public int StartLine { get; set; }
        public int EndLine { get; set; }
    }
}
