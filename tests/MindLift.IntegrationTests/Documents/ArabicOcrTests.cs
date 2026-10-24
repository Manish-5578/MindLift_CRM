using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using MindLift.Infrastructure.Documents.Text;
using Shouldly;

namespace MindLift.IntegrationTests.Documents;

/// <summary>
/// Arabic pages, read by the real Tesseract with the Arabic model. The pictures are real renderings of Arabic business lines (shaped and joined as
/// Windows draws them), kept as files so the measure does not drift. The first is clean; the second is a poor scan (blurred and speckled).
/// </summary>
public sealed class ArabicOcrTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindLift.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string Fixture(string name) => Path.Combine(Repo(), "tests", "MindLift.IntegrationTests", "Documents", "Fixtures", name);

    private static DocumentPageReader Reader()
    {
        var options = Options.Create(new OcrOptions
        {
            TesseractPath = @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            TessdataPath = Path.Combine(Repo(), "tools", "tessdata"),
            Languages = "ara+eng",
        });
        return new DocumentPageReader(new TesseractCliOcrEngine(options, NullLogger<TesseractCliOcrEngine>.Instance), options, NullLogger<DocumentPageReader>.Instance);
    }

    /// <summary>The characters that matter for comparing: digits written as digits, no spaces, no marks.</summary>
    private static string Squash(string text) => new(ArabicText.Clean(text).Where(c => !char.IsWhiteSpace(c) && c != '‎' && c != '‏').ToArray());

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }

    /// <summary>How much of the expected text was read, line by line: 100 is perfect.</summary>
    private static double Accuracy(string read, IReadOnlyList<string> expected)
    {
        var lines = read.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Squash).ToList();
        double errors = 0, total = 0;
        foreach (var line in expected.Select(Squash))
        {
            // The reading line that is closest to this expected line.
            var best = lines.Count == 0 ? line.Length : lines.Min(l => Distance(line, l));
            errors += Math.Min(best, line.Length);
            total += line.Length;
        }

        return 100 * (1 - errors / total);
    }

    private static IReadOnlyList<string> Expected() => File.ReadAllLines(Fixture("arabic-page.txt")).Where(l => l.Trim().Length > 0).ToList();

    [Fact]
    public async Task A_clean_arabic_page_is_read_with_high_accuracy_and_each_line_in_reading_order()
    {
        var page = (await Reader().ReadAsync(Fixture("arabic-page.png"), CancellationToken.None)).Single();

        var accuracy = Accuracy(page.Text, Expected());
        output.WriteLine($"Clean Arabic page: {accuracy:0.0}% of the characters read correctly");
        accuracy.ShouldBeGreaterThan(88, page.Text);

        // Words come out right to left, as they are read: the line is the sentence, not the sentence backwards.
        page.Text.ShouldContain("فاتورة ضريبية رقم 10045");
        page.Text.ShouldContain("اسم العميل: شركة النور للتجارة العامة");
        // Numbers are usable: written as ordinary digits and in the right order.
        page.Text.ShouldContain("784-1990-1234567-1");
    }

    [Fact]
    public async Task A_poor_arabic_scan_is_still_read_well_enough_to_search_and_extract_from()
    {
        var page = (await Reader().ReadAsync(Fixture("arabic-page-scan.png"), CancellationToken.None)).Single();

        var accuracy = Accuracy(page.Text, Expected());
        output.WriteLine($"Poor Arabic scan: {accuracy:0.0}% of the characters read correctly");
        accuracy.ShouldBeGreaterThan(80, page.Text);
        page.Text.ShouldContain("2026/10/15");
    }

    [Fact]
    public void Direction_marks_go_arabic_digits_become_digits_and_a_reversed_emirates_id_is_put_right()
    {
        ArabicText.Clean("‎١٥ أكتوبر ٢٠٢٦").ShouldBe("15 أكتوبر 2026");
        ArabicText.Clean("۱۲۳").ShouldBe("123");
        ArabicText.Clean("بتاريخ ١0 أكتوبر").ShouldBe("بتاريخ 15 أكتوبر");   // a round zero among Arabic-Indic digits is a five
        ArabicText.Clean("رقم 2026 و 100").ShouldBe("رقم 2026 و 100");        // ordinary numbers keep their zeros
        ArabicText.Clean("٣٫٥ ،و").ShouldBe("3.5 ,و");
        ArabicText.FixLine("رقم الهوية 1-1234567-1990-784").ShouldBe("رقم الهوية 784-1990-1234567-1");
        ArabicText.FixLine("رقم الهوية 784-1990-1234567-1").ShouldBe("رقم الهوية 784-1990-1234567-1");
        ArabicText.FixLine("Emirates ID 1-1234567-1990-784").ShouldBe("Emirates ID 1-1234567-1990-784");   // an English line is left alone
    }

    [Fact]
    public void Lines_are_ordered_from_the_right_for_arabic_and_from_the_left_for_english()
    {
        var arabic = DocumentPageReader.JoinLines([("الأول", -10d, 20d, 300d), ("الثاني", -10d, 20d, 150d), ("الثالث", -10d, 20d, 10d)]);
        arabic.ShouldBe("الأول الثاني الثالث");

        var english = DocumentPageReader.JoinLines([("first", -10d, 20d, 10d), ("second", -10d, 20d, 150d), ("third", -10d, 20d, 300d)]);
        english.ShouldBe("first second third");

        // A mostly Arabic line with a number in it keeps its right-to-left order.
        DocumentPageReader.JoinLines([("المبلغ", -10d, 20d, 300d), ("١٢٥", -10d, 20d, 200d), ("درهم", -10d, 20d, 100d)]).ShouldBe("المبلغ 125 درهم");
    }
}
