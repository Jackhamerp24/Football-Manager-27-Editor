namespace Fm27Editor.App;

/// <summary>
/// One place for the picker windows' matching, so typing "man utd" finds Manchester United the same way the universal
/// search does: folded text, word prefixes, token-loose order, the leading article and the entity's short name or id.
/// </summary>
internal static class PickerSearch
{
    static readonly string[] Articles = ["le", "la", "les", "el", "los", "las", "il", "lo", "the", "de", "do", "da", "os", "as", "al", "das", "der", "die", "den"];

    public static bool Hit(string? query, string? name, string? shortName = null, int? id = null) =>
        Score(query, name, shortName, id) >= 0;

    public static int Score(string? query, string? name, string? shortName = null, int? id = null)
    {
        string q = MainWindow.Fold((query ?? "").Trim());
        if (q.Length == 0) return 0;
        if (id is { } want && int.TryParse(q, out int typed) && typed == want) return 1000;
        int score = Math.Max(ScoreText(q, name), ScoreText(q, shortName));
        if (score >= 0) return score;
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1 && Articles.Contains(tokens[0]))
        {
            string rest = string.Join(' ', tokens.Skip(1));
            return Math.Max(ScoreText(rest, name), ScoreText(rest, shortName));
        }
        return -1;
    }

    static int ScoreText(string q, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        string folded = MainWindow.Fold(text);
        if (folded == q) return 100;
        if (folded.StartsWith(q, StringComparison.Ordinal)) return 85;
        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(q, StringComparison.Ordinal))) return 65;
        if (words.Any(w => w == q)) return 55;
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1 && tokens.All(t => words.Any(w => w.StartsWith(t, StringComparison.Ordinal)))) return 45;
        if (folded.Contains(q, StringComparison.Ordinal)) return 20;
        return -1;
    }
}
