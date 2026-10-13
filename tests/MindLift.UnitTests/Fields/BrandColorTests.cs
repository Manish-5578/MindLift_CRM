using MindLift.Shared.Validation;
using Shouldly;

namespace MindLift.UnitTests.Fields;

public sealed class BrandColorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#4F46E5")]
    [InlineData("#4f46e5")]
    [InlineData("#0D9488")]
    [InlineData("#000000")]
    public void Acceptable_colours_and_empty_values_pass(string? value)
    {
        BrandColor.Validate(value).ShouldBeNull();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("4F46E5")]
    [InlineData("#4F46E")]
    [InlineData("#4F46E5FF")]
    [InlineData("#GGGGGG")]
    [InlineData("#4F46E5; background:url(x)")]
    [InlineData("javascript:alert(1)")]
    public void Anything_that_is_not_a_six_digit_hex_colour_is_rejected(string value)
    {
        BrandColor.Validate(value).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#FFFF00")]
    [InlineData("#F8FAFC")]
    [InlineData("#A3E635")]
    public void Colours_too_light_for_white_text_are_rejected(string value)
    {
        BrandColor.Validate(value).ShouldNotBeNull().ShouldContain("too light");
    }

    [Fact]
    public void Contrast_is_calculated_with_the_wcag_formula()
    {
        BrandColor.ContrastWithWhite("#FFFFFF").ShouldBe(1.0, tolerance: 0.001);
        BrandColor.ContrastWithWhite("#000000").ShouldBe(21.0, tolerance: 0.001);
    }

    [Fact]
    public void The_default_primary_has_enough_contrast()
    {
        BrandColor.ContrastWithWhite("#4F46E5").ShouldBeGreaterThan(BrandColor.MinimumContrastWithWhite);
    }
}
