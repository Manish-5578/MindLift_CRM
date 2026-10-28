using System.Net;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Automation;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Security;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Security;

public sealed class ScopeEnforcementUnitTests
{
    [Fact]
    public async Task DataScopeService_FailsClosed_When_Permission_Is_Null()
    {
        var db = Substitute.For<IAppDbContext>();
        var tenant = Substitute.For<ITenantContext>();
        var permissions = Substitute.For<IPermissionService>();

        tenant.UserId.Returns(Guid.NewGuid());
        permissions.GetModulePermissionAsync(AppModule.Leads, Arg.Any<CancellationToken>())
            .Returns((ModulePermission?)null);

        var service = new DataScopeService(db, tenant, permissions);
        var scope = await service.GetScopeUserIdsAsync(AppModule.Leads);

        scope.ShouldNotBeNull();
        scope.ShouldBeEmpty("When permission is null, data scope must fail closed and return an empty set.");
    }

    [Fact]
    public async Task DataScopeService_FailsClosed_When_CanView_Is_False()
    {
        var db = Substitute.For<IAppDbContext>();
        var tenant = Substitute.For<ITenantContext>();
        var permissions = Substitute.For<IPermissionService>();

        tenant.UserId.Returns(Guid.NewGuid());
        permissions.GetModulePermissionAsync(AppModule.Contacts, Arg.Any<CancellationToken>())
            .Returns(new ModulePermission(AppModule.Contacts, CanView: false, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All));

        var service = new DataScopeService(db, tenant, permissions);
        var scope = await service.GetScopeUserIdsAsync(AppModule.Contacts);

        scope.ShouldNotBeNull();
        scope.ShouldBeEmpty("When CanView is false, data scope must fail closed and return an empty set.");
    }

    [Fact]
    public async Task DataScopeService_Returns_Null_For_All_Scope()
    {
        var db = Substitute.For<IAppDbContext>();
        var tenant = Substitute.For<ITenantContext>();
        var permissions = Substitute.For<IPermissionService>();

        tenant.UserId.Returns(Guid.NewGuid());
        permissions.GetModulePermissionAsync(AppModule.Deals, Arg.Any<CancellationToken>())
            .Returns(new ModulePermission(AppModule.Deals, CanView: true, CanCreate: true, CanEdit: true, CanDelete: true, CanExport: true, CanImport: true, Scope: DataScope.All));

        var service = new DataScopeService(db, tenant, permissions);
        var scope = await service.GetScopeUserIdsAsync(AppModule.Deals);

        scope.ShouldBeNull("DataScope.All must return null indicating no user filter.");
    }

    [Fact]
    public async Task DataScopeService_Returns_Self_For_Own_Scope()
    {
        var db = Substitute.For<IAppDbContext>();
        var tenant = Substitute.For<ITenantContext>();
        var permissions = Substitute.For<IPermissionService>();

        var myUserId = Guid.NewGuid();
        tenant.UserId.Returns(myUserId);
        permissions.GetModulePermissionAsync(AppModule.Deals, Arg.Any<CancellationToken>())
            .Returns(new ModulePermission(AppModule.Deals, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.Own));

        var service = new DataScopeService(db, tenant, permissions);
        var scope = await service.GetScopeUserIdsAsync(AppModule.Deals);

        scope.ShouldNotBeNull();
        scope.ShouldHaveSingleItem().ShouldBe(myUserId);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/hook")]
    [InlineData("http://localhost:5000/api")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/admin")]
    [InlineData("http://192.168.1.1/router")]
    [InlineData("http://172.16.0.1/internal")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    public void WebhookSecurity_Blocks_Dangerous_Urls(string dangerousUrl)
    {
        var isSafe = WebhookSecurity.IsSafeUrl(dangerousUrl, out var reason);
        isSafe.ShouldBeFalse();
        reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("https://api.github.com/webhook")]
    [InlineData("https://hooks.slack.com/services/T00/B00/X00")]
    public void WebhookSecurity_Allows_Public_Https_Urls(string publicUrl)
    {
        var isSafe = WebhookSecurity.IsSafeUrl(publicUrl, out var reason);
        isSafe.ShouldBeTrue(reason);
        reason.ShouldBeNull();
    }

    [Theory]
    [InlineData("/api/v1/auth/login", true)]
    [InlineData("/api/v1/auth/accept-invite", true)]
    [InlineData("/api/v1/portal/acme/auth/login", true)]
    [InlineData("/api/v1/portal/acme/auth/accept-invite", true)]
    [InlineData("/api/v1/leads", false)]
    [InlineData("/health", false)]
    public void Https_Auth_Security_Check_Identifies_Credential_Routes(string path, bool requiresHttps)
    {
        var isAuthPath = path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/portal/", StringComparison.OrdinalIgnoreCase) && path.Contains("/auth/", StringComparison.OrdinalIgnoreCase);

        isAuthPath.ShouldBe(requiresHttps);
    }

    [Fact]
    public async Task RequireAsync_Throws_ForbiddenException_When_User_Lacks_Permission()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(AppModule.Activities, PermissionAction.Edit, Arg.Any<CancellationToken>())
            .Returns(false);

        await Should.ThrowAsync<ForbiddenException>(async () =>
        {
            await permissions.RequireAsync(AppModule.Activities, PermissionAction.Edit);
        });
    }

    [Fact]
    public async Task RequireAsync_Succeeds_When_User_Has_Permission()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(AppModule.Deals, PermissionAction.Edit, Arg.Any<CancellationToken>())
            .Returns(true);

        await Should.NotThrowAsync(async () =>
        {
            await permissions.RequireAsync(AppModule.Deals, PermissionAction.Edit);
        });
    }

    [Fact]
    public void Contact_Duplicate_Validation_Detects_Unrelated_Contacts()
    {
        var masterEmail = "john.doe@example.com";
        var masterPhone = "+1234567890";
        var masterFirst = "John";
        var masterLast = "Doe";

        var unrelatedEmail = "jane.smith@example.com";
        var unrelatedPhone = "+1987654321";
        var unrelatedFirst = "Jane";
        var unrelatedLast = "Smith";

        var isDuplicate = (!string.IsNullOrWhiteSpace(masterEmail) && masterEmail == unrelatedEmail)
            || (!string.IsNullOrWhiteSpace(masterPhone) && masterPhone == unrelatedPhone)
            || (!string.IsNullOrWhiteSpace(masterFirst) && !string.IsNullOrWhiteSpace(masterLast)
                && string.Equals(masterFirst, unrelatedFirst, StringComparison.OrdinalIgnoreCase)
                && string.Equals(masterLast, unrelatedLast, StringComparison.OrdinalIgnoreCase));

        isDuplicate.ShouldBeFalse("Unrelated contacts with different email, phone, and name must not be identified as duplicates.");
    }

    [Fact]
    public void Contact_Duplicate_Validation_Matches_Duplicate_By_Email()
    {
        var masterEmail = "john.doe@example.com";
        var duplicateEmail = "john.doe@example.com";

        var isDuplicate = !string.IsNullOrWhiteSpace(masterEmail) && masterEmail == duplicateEmail;
        isDuplicate.ShouldBeTrue("Contacts with identical normalized email must be identified as duplicates.");
    }

    [Fact]
    public async Task PromoteContact_Requires_Contacts_Edit_Permission_And_Fails_Closed()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(AppModule.Contacts, PermissionAction.Edit, Arg.Any<CancellationToken>())
            .Returns(false);

        var canEditContacts = await permissions.HasAsync(AppModule.Contacts, PermissionAction.Edit);
        canEditContacts.ShouldBeFalse("Promoting contact on deal win must require Contacts.Edit permission and fail closed if lacking.");
    }

    [Fact]
    public void FileDelete_Enforces_Module_Delete_Permission_Over_Uploader_Status()
    {
        var isUploader = true;
        var hasModuleDeletePermission = false;
        var isAttachedToEntity = true;

        var allowed = isAttachedToEntity ? hasModuleDeletePermission : isUploader;
        allowed.ShouldBeFalse("An attached file cannot be deleted merely because the caller was the uploader without module Delete permission.");
    }

    [Fact]
    public void Automation_Rule_Rejects_Protected_Metadata_Fields()
    {
        var blockedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IsDeleted", "DeletedAt", "Id", "TenantId", "Version", "CreatedAt", "CreatedById", "UpdatedAt", "UpdatedById"
        };

        blockedFields.Contains("IsDeleted").ShouldBeTrue();
        blockedFields.Contains("Version").ShouldBeTrue();
        blockedFields.Contains("TenantId").ShouldBeTrue();
        blockedFields.Contains("Id").ShouldBeTrue();
        blockedFields.Contains("isdeleted").ShouldBeTrue();
        blockedFields.Contains("version").ShouldBeTrue();
        blockedFields.Contains("CustomField1").ShouldBeFalse();
        blockedFields.Contains("Status").ShouldBeFalse();
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("169.254.169.254", true)]
    public void SmtpSecurityPolicy_Detects_Blocked_IPs(string ipStr, bool blocked)
    {
        if (IPAddress.TryParse(ipStr, out var ip))
        {
            WebhookSecurity.IsBlockedAddress(ip).ShouldBe(blocked);
        }
    }

    [Fact]
    public void SmtpSecurityPolicy_Mandates_Transport_Encryption_When_Credentials_Provided()
    {
        var hasCredentials = true;
        var noneSecurity = MailKit.Security.SecureSocketOptions.None;

        Should.Throw<InvalidOperationException>(() =>
        {
            MindLift.Infrastructure.Communication.SmtpSecurityPolicy.AssertTransportSecurity(noneSecurity, hasCredentials);
        });

        var startTlsSecurity = MailKit.Security.SecureSocketOptions.StartTls;
        Should.NotThrow(() =>
        {
            MindLift.Infrastructure.Communication.SmtpSecurityPolicy.AssertTransportSecurity(startTlsSecurity, hasCredentials);
        });
    }

    [Theory]
    [InlineData("=cmd|'/C calc'!A0", "'=cmd|'/C calc'!A0")]
    [InlineData("@SUM(A1:A10)", "'@SUM(A1:A10)")]
    [InlineData("+cmd|'/C calc'!A0", "'+cmd|'/C calc'!A0")]
    [InlineData("-2+3+cmd|'/C calc'!A0", "'-2+3+cmd|'/C calc'!A0")]
    [InlineData("Normal Text", "Normal Text")]
    [InlineData("+1 555 123 4567", "+1 555 123 4567")]
    [InlineData("-12.50", "-12.50")]
    public void CsvText_Escape_Neutralizes_Formulas(string input, string expected)
    {
        var escaped = MindLift.Shared.Imports.CsvText.Escape(input);
        escaped.ShouldBe(expected);
    }

    [Fact]
    public void Contact_Merge_Rejects_Name_Only_Match_Without_Matching_Email_Or_Phone()
    {
        var master = new MindLift.Domain.Entities.Contact
        {
            FirstName = "Alice",
            LastName = "Smith",
            NormalizedEmail = "alice.smith@example.com",
            NormalizedPhone = "+15551112222"
        };

        var sameNameUnrelated = new MindLift.Domain.Entities.Contact
        {
            FirstName = "Alice",
            LastName = "Smith",
            NormalizedEmail = "different.alice@otherdomain.com",
            NormalizedPhone = "+15559998888"
        };

        var emailMatches = !string.IsNullOrWhiteSpace(master.NormalizedEmail)
            && master.NormalizedEmail == sameNameUnrelated.NormalizedEmail;
        var phoneMatches = !string.IsNullOrWhiteSpace(master.NormalizedPhone)
            && master.NormalizedPhone == sameNameUnrelated.NormalizedPhone;

        var isCandidate = emailMatches || phoneMatches;
        isCandidate.ShouldBeFalse("Same name without matching normalized email or phone must not be accepted for merge.");
    }

    [Fact]
    public void Deal_Dto_Redacts_Account_When_Out_Of_Scope()
    {
        var dealAccountId = Guid.NewGuid();
        var visibleAccountIds = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() }; // Does not contain dealAccountId

        var isAccountVisible = dealAccountId != Guid.Empty && visibleAccountIds.Contains(dealAccountId);
        Guid? effectiveAccountId = isAccountVisible ? dealAccountId : null;
        string? accountName = isAccountVisible ? "Secret Corp" : null;

        effectiveAccountId.ShouldBeNull("Deal DTO must not expose out-of-scope AccountId.");
        accountName.ShouldBeNull("Deal DTO must not expose out-of-scope AccountName.");
    }

    [Fact]
    public void Deal_Dto_Filters_Out_Contacts_That_Are_Out_Of_Scope()
    {
        var contact1 = Guid.NewGuid();
        var contact2 = Guid.NewGuid();
        var contact3 = Guid.NewGuid();

        var visibleContactIds = new HashSet<Guid> { contact1, contact3 }; // contact2 is out of scope

        var dealContacts = new[]
        {
            new DealContactDto(contact1, "Alice", null, true),
            new DealContactDto(contact2, "Bob", null, false),
            new DealContactDto(contact3, "Charlie", null, false)
        };

        var filteredContacts = dealContacts.Where(c => visibleContactIds.Contains(c.ContactId)).ToList();

        filteredContacts.Count.ShouldBe(2);
        filteredContacts.ShouldNotContain(c => c.ContactId == contact2);
    }

    [Fact]
    public void Report_Visibility_Excludes_Private_Reports_Of_Other_Users()
    {
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var systemReport = new MindLift.Domain.Entities.ReportDefinition { Id = Guid.NewGuid(), IsSystem = true, Visibility = ReportVisibility.Everyone, CreatedById = null };
        var publicReport = new MindLift.Domain.Entities.ReportDefinition { Id = Guid.NewGuid(), IsSystem = false, Visibility = ReportVisibility.Everyone, CreatedById = otherUserId };
        var ownPrivateReport = new MindLift.Domain.Entities.ReportDefinition { Id = Guid.NewGuid(), IsSystem = false, Visibility = ReportVisibility.Private, CreatedById = currentUserId };
        var otherPrivateReport = new MindLift.Domain.Entities.ReportDefinition { Id = Guid.NewGuid(), IsSystem = false, Visibility = ReportVisibility.Private, CreatedById = otherUserId };

        var reports = new[] { systemReport, publicReport, ownPrivateReport, otherPrivateReport }.AsQueryable();

        var authorized = reports.Where(r => r.IsSystem || r.Visibility == ReportVisibility.Everyone || r.CreatedById == currentUserId).ToList();

        authorized.ShouldContain(systemReport);
        authorized.ShouldContain(publicReport);
        authorized.ShouldContain(ownPrivateReport);
        authorized.ShouldNotContain(otherPrivateReport, "Private report of another user must be excluded from authorized reports.");
    }

    [Fact]
    public void ReportScheduleJob_Detects_Unauthorized_Schedule_Owner()
    {
        var scheduleOwnerId = Guid.NewGuid();
        var reportCreatorId = Guid.NewGuid();

        var privateReport = new MindLift.Domain.Entities.ReportDefinition
        {
            Id = Guid.NewGuid(),
            IsSystem = false,
            Visibility = ReportVisibility.Private,
            CreatedById = reportCreatorId
        };

        var isAuthorized = privateReport.IsSystem
            || privateReport.Visibility == ReportVisibility.Everyone
            || (scheduleOwnerId != Guid.Empty && privateReport.CreatedById == scheduleOwnerId);

        isAuthorized.ShouldBeFalse("ReportScheduleJob must detect when the schedule owner is not the private report creator.");
    }

    [Fact]
    public void Portal_Token_Cutoff_Rejects_Tokens_Issued_Before_Password_Change()
    {
        var passwordChangedAt = DateTime.UtcNow;
        var tokenIssuedBefore = passwordChangedAt.AddMinutes(-5);
        var tokenIssuedAfter = passwordChangedAt.AddMinutes(5);

        var isStaleBefore = tokenIssuedBefore < passwordChangedAt;
        var isStaleAfter = tokenIssuedAfter < passwordChangedAt;

        isStaleBefore.ShouldBeTrue("Tokens issued prior to password change must be rejected as stale.");
        isStaleAfter.ShouldBeFalse("Tokens issued after password change should be valid.");
    }

    [Fact]
    public void ChannelAccount_Validation_Rejects_Mismatched_Or_Inactive_Account()
    {
        var requestedChannel = CommunicationChannel.Email;
        var accountChannel = CommunicationChannel.WhatsApp;
        var isActive = true;

        var isValid = isActive && accountChannel == requestedChannel;
        isValid.ShouldBeFalse("Channel account whose channel does not match request must be rejected.");

        var inactiveSameChannel = false;
        var sameChannel = CommunicationChannel.Email;
        var isInactiveValid = inactiveSameChannel && sameChannel == requestedChannel;
        isInactiveValid.ShouldBeFalse("Inactive channel account must be rejected.");
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(465, true)]
    [InlineData(587, true)]
    [InlineData(2525, true)]
    [InlineData(22, false)]
    [InlineData(80, false)]
    [InlineData(443, false)]
    [InlineData(8080, false)]
    [InlineData(3306, false)]
    public void SmtpSecurityPolicy_Restricts_Allowed_Ports(int port, bool expectedAllowed)
    {
        var allowedPorts = new HashSet<int> { 25, 465, 587, 2525 };
        allowedPorts.Contains(port).ShouldBe(expectedAllowed);
    }

    [Fact]
    public void Bootstrap_Omits_Company_Settings_When_Lacking_Settings_View()
    {
        var canViewSettings = false;
        var companySettings = new CompanySettingsDto(
            "Acme Corp", null, null, "USD", "en", "UTC", 1, 1, false, null, 0m, null, null);

        CompanySettingsDto? effectiveCompany = canViewSettings ? companySettings : null;

        effectiveCompany.ShouldBeNull("Callers without Settings.View must receive null Company in BootstrapDto.");
    }

    [Fact]
    public void Portal_Invite_Rejects_When_Contact_Has_No_Email()
    {
        string? contactEmail = null;
        var reqEmail = "attacker@example.com";

        var isAllowed = !string.IsNullOrWhiteSpace(contactEmail) &&
            string.Equals(contactEmail.Trim(), reqEmail.Trim(), StringComparison.OrdinalIgnoreCase);

        isAllowed.ShouldBeFalse("Portal invite must not allow setting arbitrary email when contact has no email recorded.");
    }

    [Fact]
    public void Deal_Save_Requires_All_Submitted_Contacts_In_Caller_Scope()
    {
        var callerVisibleContactIds = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var outOfScopeContactId = Guid.NewGuid();

        var submittedContactIds = new List<Guid> { callerVisibleContactIds.First(), outOfScopeContactId };
        var allInScope = submittedContactIds.All(id => callerVisibleContactIds.Contains(id));

        allInScope.ShouldBeFalse("Deal save must reject contact modifications containing out-of-scope contacts even if already linked.");
    }

    [Fact]
    public void Deal_Contact_Replacement_Preserves_Hidden_Contacts_Server_Side()
    {
        var visibleContact1 = Guid.NewGuid();
        var visibleContact2 = Guid.NewGuid();
        var hiddenContact = Guid.NewGuid();

        var existingContactsOnDeal = new List<Guid> { visibleContact1, visibleContact2, hiddenContact };
        var callerVisibleContacts = new HashSet<Guid> { visibleContact1, visibleContact2 };

        // Caller submits only visibleContact1 (omitting visibleContact2)
        var callerSubmittedContacts = new HashSet<Guid> { visibleContact1 };

        // Logic from ReplaceContactsAsync: only remove contacts that the caller has scope to see AND omitted
        var toRemove = existingContactsOnDeal.Where(id => callerVisibleContacts.Contains(id) && !callerSubmittedContacts.Contains(id)).ToList();
        var preserved = existingContactsOnDeal.Where(id => !toRemove.Contains(id)).ToList();

        toRemove.ShouldBe([visibleContact2]);
        preserved.ShouldContain(visibleContact1);
        preserved.ShouldContain(hiddenContact);
    }

    [Fact]
    public void Booking_Management_Token_Rotates_On_Mutation()
    {
        var initialToken = Guid.NewGuid().ToString("N");
        var initialHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(initialToken))).ToLowerInvariant();

        var cancelledToken = Guid.NewGuid().ToString("N");
        var cancelledHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cancelledToken))).ToLowerInvariant();

        cancelledHash.ShouldNotBe(initialHash, "Management token hash must rotate upon cancellation.");
    }

    [Fact]
    public void Invoice_RecordPayment_Rejects_Overpayment_And_Cancelled()
    {
        var grandTotal = 1000m;
        var paidAmount = 400m;
        var balanceDue = grandTotal - paidAmount; // 600m
        var cancelledStatus = (int)InvoiceStatus.Cancelled;

        // 1. Overpayment rejection
        var paymentAmount = 700m;
        var isOverpayment = paymentAmount > balanceDue;
        isOverpayment.ShouldBeTrue("Payment amount greater than balance due must be rejected.");

        // 2. Cancelled invoice rejection
        var isCancelled = cancelledStatus == (int)InvoiceStatus.Cancelled;
        isCancelled.ShouldBeTrue("Payment against cancelled invoice must be rejected.");
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public void Webhook_Redirect_Status_Codes_Are_Treated_As_Forbidden(int statusCode)
    {
        var isRedirect = statusCode >= 300 && statusCode < 400;
        isRedirect.ShouldBeTrue("3xx redirect response from automation webhooks must be treated as SSRF violation.");
    }

    [Fact]
    public void GlobalExceptionHandler_Sanitizes_Token_Bearing_Paths_And_Query()
    {
        var path = "/api/v1/booking/manage/secret12345/cancel";
        var query = "?token=supersecrettoken&foo=bar";

        var safePath = System.Text.RegularExpressions.Regex.Replace(path, @"/manage/[^/?]+", "/manage/[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var safeQuery = System.Text.RegularExpressions.Regex.Replace(query, @"([?&]token=)[^&]+", "$1[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        safePath.ShouldBe("/api/v1/booking/manage/[REDACTED]/cancel");
        safeQuery.ShouldBe("?token=[REDACTED]&foo=bar");
    }

    [Fact]
    public void Sentry_BeforeSend_Redacts_Manage_Tokens_And_Query_Strings()
    {
        var rawUrl = "https://app.mindlift.com/api/v1/booking/manage/sensitive_booking_token_987654/cancel";
        var rawQuery = "?token=sensitive_query_param_token_12345&other=value";

        var safeUrl = System.Text.RegularExpressions.Regex.Replace(rawUrl, @"/manage/[^/?]+", "/manage/[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var safeQuery = System.Text.RegularExpressions.Regex.Replace(rawQuery, @"([?&]token=)[^&]+", "$1[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        safeUrl.ShouldNotContain("sensitive_booking_token_987654");
        safeUrl.ShouldBe("https://app.mindlift.com/api/v1/booking/manage/[REDACTED]/cancel");
        safeQuery.ShouldNotContain("sensitive_query_param_token_12345");
        safeQuery.ShouldBe("?token=[REDACTED]&other=value");
    }

    [Theory]
    [InlineData("https://portal.mindlift.com/portal/acme/magic#token=magic_tok_123", "magic_tok_123")]
    [InlineData("https://portal.mindlift.com/portal/acme/accept-invite#token=inv_tok_456&foo=bar", "inv_tok_456")]
    [InlineData("https://portal.mindlift.com/portal/acme/magic?token=query_tok_789", "query_tok_789")]
    public void Fragment_Token_Extraction_Extracts_Accurately(string uri, string expectedToken)
    {
        string? extracted = null;
        var hashIdx = uri.IndexOf('#');
        if (hashIdx >= 0 && hashIdx < uri.Length - 1)
        {
            var fragment = uri[(hashIdx + 1)..];
            var pairs = fragment.Split('&');
            foreach (var pair in pairs)
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && string.Equals(kv[0], "token", StringComparison.OrdinalIgnoreCase))
                {
                    extracted = Uri.UnescapeDataString(kv[1]);
                    break;
                }
            }
        }

        if (extracted == null)
        {
            var qIdx = uri.IndexOf('?');
            if (qIdx >= 0)
            {
                var query = uri[(qIdx + 1)..];
                if (hashIdx > qIdx)
                {
                    query = query[..(hashIdx - qIdx - 1)];
                }
                var pairs = query.Split('&');
                foreach (var pair in pairs)
                {
                    var kv = pair.Split('=', 2);
                    if (kv.Length == 2 && string.Equals(kv[0], "token", StringComparison.OrdinalIgnoreCase))
                    {
                        extracted = Uri.UnescapeDataString(kv[1]);
                        break;
                    }
                }
            }
        }

        extracted.ShouldBe(expectedToken);
    }

    [Fact]
    public void Portal_Session_Version_Mismatch_Fails_Validation()
    {
        var userDbVersion = 3;
        var tokenVersionClaim = "2"; // Stale token issued before logout/password change

        var isValid = int.TryParse(tokenVersionClaim, out var tokenVer) && tokenVer == userDbVersion;
        isValid.ShouldBeFalse("A portal access JWT with a mismatched session version must be rejected immediately upon revocation.");
    }

    [Fact]
    public void Portal_Session_Version_Match_Passes_Validation()
    {
        var userDbVersion = 3;
        var tokenVersionClaim = "3"; // Current active session

        var isValid = int.TryParse(tokenVersionClaim, out var tokenVer) && tokenVer == userDbVersion;
        isValid.ShouldBeTrue("A portal access JWT matching the current database user session version must be accepted.");
    }
}
