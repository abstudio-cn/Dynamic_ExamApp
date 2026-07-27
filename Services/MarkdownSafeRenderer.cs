using System.Text;
using System.Text.RegularExpressions;

namespace ExamApp.Services;

public class MarkdownSafeRenderer
{
    /// <summary>
    /// Render markdown text to safe HTML, preventing script injection.
    /// Only allows basic formatting: bold, italic, lists, paragraphs, line breaks.
    /// All HTML tags in the source text are escaped.
    /// </summary>
    public string RenderToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        // First, escape all HTML entities to prevent injection
        var safe = System.Net.WebUtility.HtmlEncode(markdown);

        // Restore intentional <BR> line breaks from source (case-insensitive)
        safe = Regex.Replace(safe, @"&lt;BR&gt;", "<br>", RegexOptions.IgnoreCase);
        safe = Regex.Replace(safe, @"&lt;br\s*/?&gt;", "<br>", RegexOptions.IgnoreCase);

        var sb = new StringBuilder();
        var lines = safe.Split('\n');
        bool inList = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
            {
                if (inList)
                {
                    sb.AppendLine("</ul>");
                    inList = false;
                }
                continue;
            }

            // Bold: **text** or __text__
            line = Regex.Replace(line, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            line = Regex.Replace(line, @"__(.+?)__", "<strong>$1</strong>");

            // Italic: *text* or _text_
            line = Regex.Replace(line, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<em>$1</em>");

            // Inline code: `text`
            line = Regex.Replace(line, @"`([^`]+)`", "<code>$1</code>");

            // Headers
            if (line.StartsWith("### "))
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine($"<h4>{line[4..]}</h4>");
                continue;
            }
            if (line.StartsWith("## "))
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine($"<h3>{line[3..]}</h3>");
                continue;
            }
            if (line.StartsWith("# "))
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine($"<h2>{line[2..]}</h2>");
                continue;
            }

            // Horizontal rule
            if (line.Trim() == "---" || line.Trim() == "***")
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine("<hr>");
                continue;
            }

            // Unordered list items
            if (Regex.IsMatch(line.TrimStart(), @"^[\-\*\+]\s"))
            {
                if (!inList)
                {
                    sb.AppendLine("<ul>");
                    inList = true;
                }
                var item = Regex.Replace(line.TrimStart(), @"^[\-\*\+]\s+", "");
                sb.AppendLine($"<li>{item}</li>");
                continue;
            }

            // Numbered list items: (1) or 1.
            if (Regex.IsMatch(line.TrimStart(), @"^\(\d+\)\s") || Regex.IsMatch(line.TrimStart(), @"^\d+[.、)]\s"))
            {
                if (!inList)
                {
                    sb.AppendLine("<ul>");
                    inList = true;
                }
                var item = Regex.Replace(line.TrimStart(), @"^(?:\(\d+\)|\d+[.、)])\s+", "");
                sb.AppendLine($"<li>{item}</li>");
                continue;
            }

            // Regular paragraph
            if (inList)
            {
                sb.AppendLine("</ul>");
                inList = false;
            }
            sb.AppendLine($"<p>{line}</p>");
        }

        if (inList)
        {
            sb.AppendLine("</ul>");
        }

        return sb.ToString().Trim();
    }
}
