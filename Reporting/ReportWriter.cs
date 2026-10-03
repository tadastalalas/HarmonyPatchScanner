using System;
using System.Collections.Generic;
using System.Text;

namespace HarmonyPatchScanner.Reporting
{
    /// <summary>Plain-text layout helpers: rules, wrapped paragraphs and aligned tables, all ≤ Width columns where possible.</summary>
    internal sealed class ReportWriter
    {
        internal const int Width = 100;

        private readonly StringBuilder _sb = new();

        internal void Line(string text = "") => _sb.AppendLine(text.TrimEnd());

        internal void Blank() => _sb.AppendLine();

        internal void Rule(char c = '─') => _sb.AppendLine(new string(c, Width));

        /// <summary>Report title block.</summary>
        internal void Title(string text)
        {
            Rule('═');
            Line("  " + text);
            Rule('═');
        }

        /// <summary>Section heading, preceded by a blank line.</summary>
        internal void Section(string text)
        {
            Blank();
            Line(text.ToUpperInvariant());
            Rule();
        }

        /// <summary>Writes <paramref name="text"/> word-wrapped. The first line starts with <paramref name="first"/>; continuation lines with <paramref name="indent"/>.</summary>
        internal void Wrap(string first, string indent, string text)
        {
            int available = Math.Max(20, Width - Math.Max(first.Length, indent.Length));
            List<string> lines = WrapText(text, available);
            for (int i = 0; i < lines.Count; i++)
                Line((i == 0 ? first : indent) + lines[i]);
        }

        internal void Wrap(string indent, string text) => Wrap(indent, indent, text);

        /// <summary>Aligned columns. Null header omits the heading row.</summary>
        internal void Table(string indent, string[]? header, IList<string[]> rows)
        {
            if (rows.Count == 0 && header == null) return;

            int columns = header?.Length ?? rows[0].Length;
            int[] widths = new int[columns];

            if (header != null)
                for (int c = 0; c < columns; c++)
                    widths[c] = header[c].Length;

            foreach (string[] row in rows)
                for (int c = 0; c < columns && c < row.Length; c++)
                    widths[c] = Math.Max(widths[c], row[c].Length);

            if (header != null)
            {
                Line(indent + Format(header, widths));
                StringBuilder dash = new();
                for (int c = 0; c < columns; c++)
                {
                    if (c > 0) dash.Append("  ");
                    dash.Append(new string('─', widths[c]));
                }
                Line(indent + dash);
            }

            foreach (string[] row in rows)
                Line(indent + Format(row, widths));
        }

        private static string Format(string[] cells, int[] widths)
        {
            StringBuilder sb = new();
            for (int c = 0; c < widths.Length; c++)
            {
                string cell = c < cells.Length ? cells[c] : string.Empty;
                if (c > 0) sb.Append("  ");
                sb.Append(c == widths.Length - 1 ? cell : cell.PadRight(widths[c]));
            }
            return sb.ToString();
        }

        internal static List<string> WrapText(string text, int width)
        {
            List<string> lines = [];
            StringBuilder current = new();
            foreach (string word in text.Split(' '))
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > width)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }
            if (current.Length > 0 || lines.Count == 0) lines.Add(current.ToString());
            return lines;
        }

        public override string ToString() => _sb.ToString();
    }
}
