using FluentValidation;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Validation;

/// <summary>Input rules for requests. Each rule mirrors a rule in the plan (sections 4.3, 6.2, 7.1).</summary>
public sealed class SignupRequestValidator : AbstractValidator<SignupRequest>
{
    public SignupRequestValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.Country).NotEmpty().Length(2);
        RuleFor(x => x.Language).Must(l => l is "en" or "ar").WithMessage("Language must be en or ar.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class CreateInvitationRequestValidator : AbstractValidator<CreateInvitationRequest>
{
    public CreateInvitationRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public sealed class SaveRoleRequestValidator : AbstractValidator<SaveRoleRequest>
{
    public SaveRoleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class SaveTeamRequestValidator : AbstractValidator<SaveTeamRequest>
{
    public SaveTeamRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class SaveBranchRequestValidator : AbstractValidator<SaveBranchRequest>
{
    public SaveBranchRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Address).MaximumLength(300);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Language).Must(l => l is "en" or "ar").WithMessage("Language must be en or ar.");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

/// <summary>
/// Size limits for lead input. The public web form is anonymous, so without these a visitor could store megabytes of text,
/// and a value longer than a database column used to fail with a server error instead of a field error.
/// </summary>
public sealed class SaveContactRequestValidator : AbstractValidator<SaveContactRequest>
{
    public SaveContactRequestValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(100);
        RuleFor(x => x.LastName).MaximumLength(100);
        RuleFor(x => x.Email).MaximumLength(320);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.JobTitle).MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(5000);
        RuleFor(x => x.Type).MaximumLength(30);
    }
}

public sealed class SaveDealRequestValidator : AbstractValidator<SaveDealRequest>
{
    public SaveDealRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Currency).MaximumLength(3);
        RuleFor(x => x.Description).MaximumLength(5000);
        RuleForEach(x => x.Contacts).ChildRules(c => c.RuleFor(i => i.Role).MaximumLength(100));
    }
}

public sealed class ChangeDealStageRequestValidator : AbstractValidator<ChangeDealStageRequest>
{
    public ChangeDealStageRequestValidator()
    {
        RuleFor(x => x.LostReason).MaximumLength(60);
        RuleFor(x => x.LostNotes).MaximumLength(2000);
    }
}

public sealed class SaveActivityRequestValidator : AbstractValidator<SaveActivityRequest>
{
    public SaveActivityRequestValidator()
    {
        RuleFor(x => x.Type).MaximumLength(20);
        RuleFor(x => x.Subject).MaximumLength(250);
        RuleFor(x => x.Description).MaximumLength(10000);
        RuleFor(x => x.Priority).MaximumLength(10);
        RuleFor(x => x.Direction).MaximumLength(10);
        RuleFor(x => x.CallOutcome).MaximumLength(60);
        RuleFor(x => x.Location).MaximumLength(300);
    }
}

public sealed class CompleteActivityRequestValidator : AbstractValidator<CompleteActivityRequest>
{
    public CompleteActivityRequestValidator()
    {
        RuleFor(x => x.Outcome).MaximumLength(60);
        RuleFor(x => x.Notes).MaximumLength(10000);
    }
}

public sealed class SaveAccountRequestValidator : AbstractValidator<SaveAccountRequest>
{
    public SaveAccountRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Website).MaximumLength(300);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Industry).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(5000);
    }
}

public sealed class SaveLeadRequestValidator : AbstractValidator<SaveLeadRequest>
{
    public SaveLeadRequestValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(100);
        RuleFor(x => x.LastName).MaximumLength(100);
        RuleFor(x => x.Email).MaximumLength(320);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.CompanyName).MaximumLength(200);
        RuleFor(x => x.Status).MaximumLength(30);
        RuleFor(x => x.Source).MaximumLength(60);
        RuleFor(x => x.Rating).MaximumLength(30);
        RuleFor(x => x.Description).MaximumLength(5000);
        RuleFor(x => x.Tags).Must(t => t is null || t.Count <= 20).WithMessage("Use at most 20 tags.");
        RuleForEach(x => x.Tags).MaximumLength(40);
    }
}

public sealed class PublicFormSubmissionValidator : AbstractValidator<PublicFormSubmission>
{
    public PublicFormSubmissionValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(100);
        RuleFor(x => x.LastName).MaximumLength(100);
        RuleFor(x => x.Email).MaximumLength(320);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.CompanyName).MaximumLength(200);
        RuleFor(x => x.Message).MaximumLength(5000);
        RuleFor(x => x.Hp).MaximumLength(200);
    }
}

public sealed class CheckDuplicatesRequestValidator : AbstractValidator<CheckDuplicatesRequest>
{
    public CheckDuplicatesRequestValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(100);
        RuleFor(x => x.LastName).MaximumLength(100);
        RuleFor(x => x.Email).MaximumLength(320);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.CompanyName).MaximumLength(200);
    }
}

public sealed class DisqualifyLeadRequestValidator : AbstractValidator<DisqualifyLeadRequest>
{
    public DisqualifyLeadRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class ConvertLeadRequestValidator : AbstractValidator<ConvertLeadRequest>
{
    public ConvertLeadRequestValidator()
    {
        RuleFor(x => x.AccountName).MaximumLength(200);
        RuleFor(x => x.DealName).MaximumLength(200);
        RuleFor(x => x.Currency).MaximumLength(3);
    }
}

public sealed class MergeLeadsRequestValidator : AbstractValidator<MergeLeadsRequest>
{
    public MergeLeadsRequestValidator()
    {
        RuleFor(x => x.DuplicateIds).NotNull().Must(ids => ids is not null && ids.Count is >= 1 and <= 50).WithMessage("Choose between 1 and 50 leads to merge.");
    }
}

public sealed class SaveAssignmentRuleRequestValidator : AbstractValidator<SaveAssignmentRuleRequest>
{
    public SaveAssignmentRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UserIds).NotNull().Must(ids => ids is not null && ids.Count <= 100).WithMessage("A rule can have at most 100 people.");
    }
}

public sealed class SaveWebFormRequestValidator : AbstractValidator<SaveWebFormRequest>
{
    public SaveWebFormRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DefaultSource).MaximumLength(60);
        RuleFor(x => x.SuccessMessage).MaximumLength(500);
        RuleFor(x => x.Fields).NotNull().Must(f => f is not null && f.Count <= 10).WithMessage("A form can collect at most 10 fields.");
    }
}

public sealed class SaveAutomationRuleRequestValidator : AbstractValidator<SaveAutomationRuleRequest>
{
    public SaveAutomationRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Priority).InclusiveBetween(1, 1000);
        RuleFor(x => x.Actions).NotNull().Must(a => a is not null && a.Count is >= 1 and <= 20)
            .WithMessage("A rule must have between 1 and 20 actions.");
    }
}

/// <summary>Limits for the settings screens. They match the database columns, so too much text is a field error and not a server error.</summary>
public sealed class SaveCompanySettingsRequestValidator : AbstractValidator<SaveCompanySettingsRequest>
{
    public SaveCompanySettingsRequestValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(320);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.DefaultLanguage).MaximumLength(5);
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.TaxLabel).MaximumLength(30);
        RuleFor(x => x.TaxRegistrationNumber).MaximumLength(50);
        RuleFor(x => x.PrimaryColor).MaximumLength(7);
    }
}

public sealed class OnboardingCompanyRequestValidator : AbstractValidator<OnboardingCompanyRequest>
{
    public OnboardingCompanyRequestValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Country).MaximumLength(2);
        RuleFor(x => x.BaseCurrency).MaximumLength(3);
        RuleFor(x => x.TimeZoneId).MaximumLength(64);
        RuleFor(x => x.DefaultLanguage).MaximumLength(5);
        RuleFor(x => x.TaxLabel).MaximumLength(30);
        RuleFor(x => x.TaxRegistrationNumber).MaximumLength(50);
    }
}

public sealed class SaveModuleSettingRequestValidator : AbstractValidator<SaveModuleSettingRequest>
{
    public SaveModuleSettingRequestValidator()
    {
        RuleFor(x => x.SingularLabel).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PluralLabel).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000);
    }
}

public sealed class SavePicklistValueRequestValidator : AbstractValidator<SavePicklistValueRequest>
{
    public SavePicklistValueRequestValidator()
    {
        RuleFor(x => x.Value).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(120);
        RuleFor(x => x.LabelAr).MaximumLength(120);
        RuleFor(x => x.Category).MaximumLength(40);
        RuleFor(x => x.Color).MaximumLength(9);
    }
}

public sealed class SaveCustomFieldRequestValidator : AbstractValidator<SaveCustomFieldRequest>
{
    public SaveCustomFieldRequestValidator()
    {
        RuleFor(x => x.Key).MaximumLength(50);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LabelAr).MaximumLength(100);
        RuleFor(x => x.Section).MaximumLength(100);
        RuleFor(x => x.Options).Must(o => o is null || o.Count <= 200).WithMessage("A choice field can have at most 200 options.");
        RuleForEach(x => x.Options).ChildRules(option =>
        {
            option.RuleFor(o => o.Value).MaximumLength(60);
            option.RuleFor(o => o.Label).MaximumLength(120);
            option.RuleFor(o => o.LabelAr).MaximumLength(120);
        });
    }
}
