using System.Text;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Splits markdown playbooks into retrieval units.
/// Boundary = H1/H2 headings so each chunk stays a coherent procedure, not a random token window.
/// </summary>
public static class KnowledgeChunker
{
    public static IReadOnlyList<KnowledgeChunk> ChunkDocument(string playbookId, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var chunks = new List<KnowledgeChunk>();
        string? currentTitle = null;
        var body = new StringBuilder();
        var sectionIndex = 0;

        void Flush()
        {
            var content = body.ToString().Trim();
            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(currentTitle))
            {
                return;
            }

            var title = string.IsNullOrWhiteSpace(currentTitle)
                ? playbookId
                : currentTitle.Trim();
            chunks.Add(new KnowledgeChunk
            {
                Id = $"{playbookId}#{sectionIndex}",
                PlaybookId = playbookId,
                Title = title,
                Content = content.Length == 0 ? title : content
            });
            sectionIndex++;
            body.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("# ", StringComparison.Ordinal) ||
                line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                currentTitle = line.TrimStart('#').Trim();
                continue;
            }

            body.AppendLine(line);
        }

        Flush();
        return chunks;
    }
}
