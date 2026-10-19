using System.Text;

namespace IWMS.Api.Reports;

public static class SimplePdfWriter
{
    public static byte[] Create(IEnumerable<string> lines)
    {
        var pages = lines.Chunk(45).ToArray();
        if (pages.Length == 0) pages = new[] { Array.Empty<string>() };

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"
        };
        var pageIds = new List<int>();

        foreach (var pageLines in pages)
        {
            var pageId = objects.Count + 1;
            var streamId = pageId + 1;
            pageIds.Add(pageId);
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 3 0 R >> >> /Contents {streamId} 0 R >>");

            var stream = new StringBuilder("BT\n/F1 8 Tf\n30 565 Td\n11 TL\n");
            foreach (var line in pageLines)
            {
                var safe = new string(line.Select(c => c >= ' ' && c <= '~' ? c : '?').ToArray());
                safe = safe.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                stream.Append('(').Append(safe).Append(") Tj\nT*\n");
            }
            stream.Append("ET");
            var body = stream.ToString();
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(body)} >>\nstream\n{body}\nendstream");
        }

        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>";
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
            pdf.Append($"{offsets[i]:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}