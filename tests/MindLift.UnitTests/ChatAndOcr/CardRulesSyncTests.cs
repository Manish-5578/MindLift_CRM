using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MindLift.Infrastructure.Leads;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.ChatAndOcr;

/// <summary>
/// The card reading rules live in shared/card-rules.json and are generated into both the server and the phone app.
/// These tests fail when the generated server copy is out of date; the phone app has the same test against the same file.
/// Fix a failure by running: python scripts/gen-card-rules.py
/// </summary>
public sealed class CardRulesSyncTests
{
    private static JsonElement LoadRules()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "shared", "card-rules.json")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("shared/card-rules.json was not found above the test folder.");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "shared", "card-rules.json")));
        return doc.RootElement.Clone();
    }

    private static string[] List(JsonElement rules, string key) => rules.GetProperty(key).EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public void Server_word_lists_match_the_shared_file()
    {
        var rules = LoadRules();

        CardRules.StrongAddressTokens.ShouldBe(List(rules, "strongAddressTokens"));
        CardRules.WeakAddressTokens.ShouldBe(List(rules, "weakAddressTokens"));
        CardRules.PlaceNames.ShouldBe(List(rules, "placeNames"));
        CardRules.AddressLabels.ShouldBe(List(rules, "addressLabels"));
        CardRules.OtherLabels.ShouldBe(List(rules, "otherLabels"));
        CardRules.Honorifics.ShouldBe(List(rules, "honorifics"));
        CardRules.TitleKeywords.ShouldBe(List(rules, "titleKeywords"));
        CardRules.CompanyKeywords.ShouldBe(List(rules, "companyKeywords"));
    }

    [Fact]
    public void Server_patterns_match_the_shared_file()
    {
        var patterns = LoadRules().GetProperty("patterns");

        CardRules.NoisePattern.ShouldBe(patterns.GetProperty("noise").GetString());
        CardRules.FoundedPattern.ShouldBe(patterns.GetProperty("founded").GetString());
        CardRules.SocialSitePattern.ShouldBe(patterns.GetProperty("socialSite").GetString());
    }

    [Fact]
    public void Every_list_has_words_and_no_blanks()
    {
        var rules = LoadRules();
        foreach (var key in new[] { "strongAddressTokens", "weakAddressTokens", "placeNames", "addressLabels", "otherLabels", "honorifics", "titleKeywords", "companyKeywords" })
        {
            var words = List(rules, key);
            words.ShouldNotBeEmpty(key);
            words.ShouldAllBe(w => !string.IsNullOrWhiteSpace(w), key);
            words.Select(w => w.ToLowerInvariant()).Distinct().Count().ShouldBe(words.Length, $"{key} has a repeated word");
        }
    }
}
