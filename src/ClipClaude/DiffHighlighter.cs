using System.Text.RegularExpressions;

namespace ClipClaude;

public sealed record DiffResult(IReadOnlyList<(int Start, int Length)> ChangedRanges, int ChangeCount);

/// <summary>Пословный diff исходного и исправленного текста (LCS по токенам), считается локально.</summary>
public static class DiffHighlighter
{
    private static readonly Regex TokenPattern = new(@"\w+|\s+|[^\w\s]", RegexOptions.CultureInvariant);

    /// <summary>Ограничение на размер таблицы LCS (после отсечения общего начала и конца).</summary>
    private const long MaxCells = 5_000_000; // ~20 МБ на время расчёта

    private readonly record struct Token(string Text, int Start)
    {
        public bool IsSpace => Text.Length > 0 && char.IsWhiteSpace(Text[0]);
    }

    public static DiffResult Compute(string original, string corrected)
    {
        var a = Tokenize(original);
        var b = Tokenize(corrected);

        int prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix].Text == b[prefix].Text) prefix++;
        int suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix &&
               a[a.Count - 1 - suffix].Text == b[b.Count - 1 - suffix].Text) suffix++;

        int n = a.Count - prefix - suffix;
        int m = b.Count - prefix - suffix;

        // Операции: true = элемент b совпал, false = вставлен/изменён; для a — аналогично.
        var bMatched = new bool[m];
        var aMatched = new bool[n];

        if (n > 0 && m > 0 && (long)(n + 1) * (m + 1) <= MaxCells)
        {
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[prefix + i].Text == b[prefix + j].Text
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[prefix + x].Text == b[prefix + y].Text) { aMatched[x++] = true; bMatched[y++] = true; }
                else if (lcs[x + 1, y] >= lcs[x, y + 1]) x++;
                else y++;
            }
        }

        // Подсвечиваемые диапазоны в исправленном тексте (без пробельных токенов).
        var ranges = new List<(int, int)>();
        for (int j = 0; j < m; j++)
        {
            var t = b[prefix + j];
            if (bMatched[j] || t.IsSpace) continue;
            int start = t.Start, end = t.Start + t.Text.Length;
            // склеиваем соседние изменённые токены (через пробелы) в один диапазон
            while (j + 1 < m && !bMatched[j + 1])
            {
                j++;
                end = b[prefix + j].Start + b[prefix + j].Text.Length;
            }
            // хвостовые пробелы не подсвечиваем
            while (end > start && char.IsWhiteSpace(corrected[end - 1])) end--;
            ranges.Add((start, end - start));
        }

        return new DiffResult(ranges, CountHunks(a, b, prefix, aMatched, bMatched));
    }

    /// <summary>Число правок: группы подряд идущих несовпадений, в которых есть непробельный токен.</summary>
    private static int CountHunks(List<Token> a, List<Token> b, int prefix, bool[] aMatched, bool[] bMatched)
    {
        int count = 0, x = 0, y = 0;
        while (x < aMatched.Length || y < bMatched.Length)
        {
            bool inHunk = false, meaningful = false;
            while (x < aMatched.Length && !aMatched[x]) { inHunk = true; meaningful |= !a[prefix + x].IsSpace; x++; }
            while (y < bMatched.Length && !bMatched[y]) { inHunk = true; meaningful |= !b[prefix + y].IsSpace; y++; }
            if (inHunk && meaningful) count++;
            if (!inHunk) { x++; y++; }
        }
        return count;
    }

    private static List<Token> Tokenize(string s) =>
        TokenPattern.Matches(s).Select(m => new Token(m.Value, m.Index)).ToList();
}
