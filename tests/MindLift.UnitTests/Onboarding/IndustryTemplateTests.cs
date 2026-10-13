using MindLift.Infrastructure.Onboarding;
using Shouldly;

namespace MindLift.UnitTests.Onboarding;

/// <summary>Plan 6.4 and 6.6: every shipped template loads and passes the same rules as the product.</summary>
public sealed class IndustryTemplateTests
{
    [Fact]
    public void All_seven_templates_are_shipped()
    {
        IndustryTemplates.All.Select(t => t.Key).Order().ShouldBe(
            ["corporate_services", "finance_consulting", "general", "healthcare", "logistics", "other", "real_estate"]);
    }

    [Theory]
    [InlineData("corporate_services")]
    [InlineData("finance_consulting")]
    [InlineData("general")]
    [InlineData("healthcare")]
    [InlineData("logistics")]
    [InlineData("other")]
    [InlineData("real_estate")]
    public void Every_template_passes_validation(string key)
    {
        var template = IndustryTemplates.Get(key)!;

        IndustryTemplateValidator.Validate(template).ShouldBeEmpty();
    }

    [Fact]
    public void Real_estate_renames_deals_to_property_deals_and_adds_its_fields()
    {
        var template = IndustryTemplates.Get("real_estate")!;

        template.Modules.Labels["Deals"].Singular.ShouldBe("Property Deal");
        template.Pipelines.Single(p => p.IsDefault).Stages.Select(s => s.Name)
            .ShouldBe(["Enquiry", "Viewing", "Offer", "Agreement", "Closed Won", "Closed Lost"]);
        template.CustomFields.Where(f => f.Entity == "leads").Select(f => f.Key)
            .ShouldBe(["cf_property_type", "cf_location", "cf_budget", "cf_bedrooms"], ignoreOrder: true);
    }

    [Fact]
    public void A_broken_template_is_reported_not_silently_accepted()
    {
        var broken = new IndustryTemplate
        {
            Key = "broken",
            Pipelines = [new TemplatePipeline { Name = "P", IsDefault = true, Stages = [new TemplateStage { Name = "Only open", Probability = 10, Type = "Open" }] }],
            CustomFields = [new TemplateCustomField { Entity = "leads", Key = "budget", Type = "Picklist" }],
        };

        var errors = IndustryTemplateValidator.Validate(broken);

        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Contains("Won and Lost", StringComparison.Ordinal) || e.Contains("exactly one Won", StringComparison.Ordinal));
        errors.ShouldContain(e => e.Contains("budget", StringComparison.Ordinal));
    }
}
