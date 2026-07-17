using System.Globalization;
using System.Text;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

internal static class SearchMatchHelper
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var compacted = string.Join(" ", value
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var normalized = compacted.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string Compact(string? value)
        => Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal);

    public static int Score(string query, params string?[] fields)
    {
        var normalizedQuery = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return 0;

        var tokens = Tokenize(query);
        var compactQuery = Compact(query);
        var score = 0;

        foreach (var field in fields)
        {
            var normalizedField = Normalize(field);
            if (string.IsNullOrWhiteSpace(normalizedField))
                continue;

            var compactField = Compact(field);
            if (normalizedField == normalizedQuery)
                score += 1000;
            else if (normalizedField.StartsWith(normalizedQuery, StringComparison.Ordinal))
                score += 700;
            else if (normalizedField.Contains(normalizedQuery, StringComparison.Ordinal))
                score += 400;

            if (!string.IsNullOrWhiteSpace(compactQuery) && !string.IsNullOrWhiteSpace(compactField))
            {
                if (compactField == compactQuery)
                    score += 500;
                else if (compactField.Contains(compactQuery, StringComparison.Ordinal))
                    score += 220;
            }

            foreach (var token in tokens)
            {
                if (normalizedField.StartsWith(token, StringComparison.Ordinal))
                    score += 120;
                else if (normalizedField.Contains(token, StringComparison.Ordinal))
                    score += 60;

                foreach (var fieldToken in Tokenize(field))
                {
                    var tokenSimilarity = CalculateSimilarity(token, fieldToken);
                    if (tokenSimilarity >= 0.92d)
                        score += 90;
                    else if (tokenSimilarity >= 0.82d)
                        score += 45;
                }
            }

            var fieldSimilarity = CalculateSimilarity(compactQuery, compactField);
            if (fieldSimilarity >= 0.96d)
                score += 350;
            else if (fieldSimilarity >= 0.88d)
                score += 180;
            else if (fieldSimilarity >= 0.78d)
                score += 80;
        }

        return score;
    }

    public static string[] BuildSearchTerms(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        var terms = new HashSet<string>(StringComparer.Ordinal)
        {
            normalized
        };

        var compact = Compact(value);
        if (!string.IsNullOrWhiteSpace(compact) && compact.Length >= 4)
            terms.Add(compact);

        foreach (var token in Tokenize(value))
        {
            if (token.Length >= 2)
                terms.Add(token);

            var singular = Singularize(token);
            if (singular.Length >= 2)
                terms.Add(singular);
        }

        return terms
            .OrderByDescending(x => x.Length)
            .Take(5)
            .ToArray();
    }

    public static string[] Tokenize(string? value)
        => Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsLikelyDuplicate(string query, params string?[] fields)
    {
        var compactQuery = Compact(query);
        if (string.IsNullOrWhiteSpace(compactQuery))
            return false;

        foreach (var field in fields)
        {
            var compactField = Compact(field);
            if (string.IsNullOrWhiteSpace(compactField))
                continue;

            if (compactField == compactQuery)
                return true;

            var similarity = CalculateSimilarity(compactQuery, compactField);
            if (similarity >= 0.88d)
                return true;

            if (Score(query, field) >= 360)
                return true;
        }

        return false;
    }

    private static string Singularize(string token)
    {
        if (token.Length <= 3)
            return token;

        if (token.EndsWith("es", StringComparison.Ordinal))
            return token[..^2];

        if (token.EndsWith("s", StringComparison.Ordinal))
            return token[..^1];

        return token;
    }

    private static double CalculateSimilarity(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return 0d;

        if (left == right)
            return 1d;

        var distance = LevenshteinDistance(left, right);
        var maxLength = Math.Max(left.Length, right.Length);
        return maxLength == 0 ? 1d : 1d - (double)distance / maxLength;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var matrix = new int[left.Length + 1, right.Length + 1];

        for (var i = 0; i <= left.Length; i++)
            matrix[i, 0] = i;

        for (var j = 0; j <= right.Length; j++)
            matrix[0, j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[left.Length, right.Length];
    }
}
