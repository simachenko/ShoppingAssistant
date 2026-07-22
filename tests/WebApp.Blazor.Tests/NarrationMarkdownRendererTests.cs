using WebApp.Blazor.Rendering;

namespace WebApp.Blazor.Tests;

public sealed class NarrationMarkdownRendererTests
{
    [Fact]
    public void Heading_renders_as_a_real_heading_element()
    {
        var html = NarrationMarkdownRenderer.ToSafeHtml("# Great pick");

        Assert.Contains("<h1>Great pick</h1>", html);
    }

    [Fact]
    public void Bold_text_renders_as_a_strong_element()
    {
        var html = NarrationMarkdownRenderer.ToSafeHtml("This is **important**.");

        Assert.Contains("<strong>important</strong>", html);
    }

    [Fact]
    public void Bullet_list_renders_as_a_real_list()
    {
        var html = NarrationMarkdownRenderer.ToSafeHtml("- First\n- Second");

        Assert.Contains("<ul>", html);
        Assert.Contains("<li>First</li>", html);
        Assert.Contains("<li>Second</li>", html);
    }

    [Fact]
    public void Raw_script_tags_are_neutralized_into_inert_escaped_text()
    {
        var html = NarrationMarkdownRenderer.ToSafeHtml("Hello<script>alert('xss')</script>");

        // DisableHtml means the LLM can never smuggle a live <script> element through — it comes
        // out HTML-encoded (harmless visible text), never as an executable tag.
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Raw_onclick_attributes_are_neutralized_into_inert_escaped_text()
    {
        var html = NarrationMarkdownRenderer.ToSafeHtml("<span onclick=\"alert('xss')\">click me</span>");

        // Same guarantee as above — no live <span ...> element ever reaches the DOM.
        Assert.DoesNotContain("<span", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Javascript_links_are_stripped()
    {
        var html = NarrationMarkdownRenderer.ToSafeHtml("[click me](javascript:alert('xss'))");

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }
}
