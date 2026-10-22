using System.Globalization;
using MindLift.Infrastructure.Cpq;

namespace MindLift.Infrastructure.Documents.Text;

/// <summary>How well one page was read. <c>Score</c> is 0–100: the engine's own confidence, lowered when much of what it found is not text at all.</summary>
public sealed record PageQuality(double Score, int Words, int Characters)
{
    public static PageQuality Empty { get; } = new(0, 0, 0);
}

public static class OcrQuality
{
    /// <summary>
    /// Scores a reading. Each word counts in proportion to its length, so a long word read with doubt matters more than a stray mark.
    /// Words with no letter or digit in them (noise read from specks, folds and borders) pull the score down by up to half.
    /// </summary>
    public static PageQuality Evaluate(IReadOnlyList<OcrWord> words)
    {
        double weighted = 0, weight = 0;
        var characters = 0;
        var junk = 0;
        foreach (var word in words)
        {
            var length = word.Text.Length;
            characters += length;
            if (!word.Text.Any(char.IsLetterOrDigit))
            {
                junk++;
            }

            weighted += Math.Clamp(word.Confidence, 0, 100) * length;
            weight += length;
        }

        if (weight == 0)
        {
            return PageQuality.Empty;
        }

        var junkShare = (double)junk / words.Count;
        var score = weighted / weight * (1 - 0.5 * junkShare);
        return new PageQuality(Math.Round(score, 1), words.Count, characters);
    }

    /// <summary>
    /// Picks the best of several readings of one page. The score decides, but a reading that found far less text than another is not trusted just because what it found was sure:
    /// the score is scaled by how much of the most text found anywhere it caught (from half to all of its weight).
    /// </summary>
    public static int Best(IReadOnlyList<PageQuality> readings)
    {
        if (readings.Count == 0)
        {
            return -1;
        }

        var most = Math.Max(1, readings.Max(r => r.Characters));
        var best = 0;
        var bestRank = double.MinValue;
        for (var i = 0; i < readings.Count; i++)
        {
            var rank = readings[i].Score * (0.5 + 0.5 * readings[i].Characters / most);
            if (rank > bestRank + 1e-9)
            {
                bestRank = rank;
                best = i;
            }
        }

        return best;
    }

    /// <summary>A short note for the logs.</summary>
    public static string Describe(PageQuality quality)
        => string.Create(CultureInfo.InvariantCulture, $"score {quality.Score:0.#}, {quality.Words} words");
}
