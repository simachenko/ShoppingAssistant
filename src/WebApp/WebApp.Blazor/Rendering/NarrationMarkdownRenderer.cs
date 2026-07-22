using Ganss.Xss;
using Markdig;

namespace WebApp.Blazor.Rendering;

/// <summary>
/// Renders the LLM's narration text as safe HTML (research.md §12) — Markdig with raw HTML
/// passthrough disabled, then a strict allow-list sanitizer pass as defense in depth. Never used
/// for structured facts (specs/matched requirements/trade-offs), which are rendered directly as
/// Razor markup from tool-produced data instead — narration is presentation-only and must never
/// be trusted to introduce a new fact (FR-016).
/// </summary>
public static class NarrationMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseEmphasisExtras()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = new();

    public static string ToSafeHtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var html = Markdown.ToHtml(markdown, Pipeline);
        return Sanitizer.Sanitize(html);
    }
}
