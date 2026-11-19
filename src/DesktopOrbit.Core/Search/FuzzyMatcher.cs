namespace DesktopOrbit.Core.Search;

public static class FuzzyMatcher
{
    public static int Score(string? text, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 1;
        if (string.IsNullOrEmpty(text)) return 0;

        int best = 0;
        int direct = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (direct >= 0)
        {
            best = 1000 - Math.Min(direct, 400);
            if (text.Length == query.Length) best += 120;
        }

        int subsequence = SubsequenceScore(text, query);
        if (subsequence > best) best = subsequence;
        return best;
    }

    private static int SubsequenceScore(string text, string query)
    {
        int textIndex = 0;
        int score = 0;
        int matched = 0;
        int lastMatch = -2;

        foreach (char raw in query)
        {
            if (char.IsWhiteSpace(raw)) continue;
            char wanted = char.ToLowerInvariant(raw);
            bool found = false;

            while (textIndex < text.Length)
            {
                if (char.ToLowerInvariant(text[textIndex]) == wanted)
                {
                    score += 10;
                    if (lastMatch == textIndex - 1) score += 16;
                    else if (IsWordStart(text, textIndex)) score += 20;
                    lastMatch = textIndex;
                    textIndex++;
                    matched++;
                    found = true;
                    break;
                }
                textIndex++;
            }

            if (!found) return 0;
        }

        return matched == 0 ? 0 : score;
    }

    private static bool IsWordStart(string text, int index)
    {
        if (index <= 0) return true;
        char previous = text[index - 1];
        if (!char.IsLetterOrDigit(previous)) return true;
        return char.IsLower(previous) && char.IsUpper(text[index]);
    }
}
