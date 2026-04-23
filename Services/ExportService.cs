using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinUtilDashboard.Services.Interfaces;

namespace WinUtilDashboard.Services;

/// <summary>
/// Exports reports to text, CSV, or HTML. CSV fields are escaped per RFC 4180
/// and HTML content is HTML-encoded to avoid broken layout from special chars.
/// </summary>
public sealed class ExportService : IExportService
{
    public Task ExportToTextAsync(string filePath, string content, CancellationToken ct = default)
        => File.WriteAllTextAsync(filePath, content, Encoding.UTF8, ct);

    public async Task ExportToCsvAsync(
        string filePath,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        AppendCsvRow(sb, headers);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            AppendCsvRow(sb, row);
        }
        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
    }

    public Task ExportToHtmlAsync(string filePath, string title, string content, CancellationToken ct = default)
    {
        string safeTitle = WebUtility.HtmlEncode(title);
        // Encode content but preserve user-intended line breaks
        string safeContent = WebUtility.HtmlEncode(content).Replace("\n", "<br>");

        var html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>{safeTitle}</title>
    <style>
        body {{ font-family: 'Segoe UI', sans-serif; margin: 20px; background: #f5f5f5; }}
        .container {{ max-width: 900px; margin: auto; background: white; padding: 30px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        h1 {{ color: #0078D4; border-bottom: 3px solid #0078D4; padding-bottom: 10px; }}
        .info {{ margin: 10px 0; padding: 10px; background: #f9f9f9; border-left: 4px solid #0078D4; white-space: pre-wrap; font-family: 'Consolas', monospace; }}
        .timestamp {{ color: #666; font-size: 0.9em; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>{safeTitle}</h1>
        <div class=""timestamp"">Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>
        <div class=""info"">{safeContent}</div>
    </div>
</body>
</html>";

        return File.WriteAllTextAsync(filePath, html, Encoding.UTF8, ct);
    }

    private static void AppendCsvRow(StringBuilder sb, IReadOnlyList<string> row)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(EscapeCsv(row[i]));
        }
        sb.Append("\r\n"); // CRLF per RFC 4180
    }

    private static string EscapeCsv(string? v)
    {
        v ??= "";
        bool needQuote = v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!needQuote) return v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }
}
