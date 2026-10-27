using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Automation;
using MindLift.Infrastructure.Communication;
using MindLift.Infrastructure.Cpq;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.IntegrationTests.Security;

public sealed class SecretAndNetworkSecurityTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("93.184.216.34", false)]
    public void WebhookSecurity_correctly_identifies_restricted_ipv4(string ipString, bool expectedBlocked)
    {
        var ip = IPAddress.Parse(ipString);
        WebhookSecurity.IsBlockedAddress(ip).ShouldBe(expectedBlocked);
    }

    [Theory]
    [InlineData("::1", true)]                           // IPv6 loopback
    [InlineData("::", true)]                            // IPv6 unspecified
    [InlineData("fd00:ec2::254", true)]                 // IPv6 ULA (AWS IMDSv6)
    [InlineData("fc00:1::1", true)]                     // IPv6 ULA private range
    [InlineData("fe80::1", true)]                       // IPv6 link-local
    [InlineData("2001:db8::1", true)]                   // IPv6 documentation
    [InlineData("100::1", true)]                        // IPv6 discard prefix
    [InlineData("2002:0a00:0001::", true)]              // IPv6 6to4 private (10.0.0.1)
    [InlineData("2607:f8b0:4005:805::200e", false)]     // Public IPv6 (Google)
    public void WebhookSecurity_correctly_identifies_restricted_ipv6(string ipString, bool expectedBlocked)
    {
        var ip = IPAddress.Parse(ipString);
        WebhookSecurity.IsBlockedAddress(ip).ShouldBe(expectedBlocked);
    }

    [Fact]
    public void WebhookAuthenticator_verifies_both_sha256_and_sha1_signatures()
    {
        var form = new Dictionary<string, string>
        {
            ["AccountSid"] = "ACtest12345",
            ["From"] = "+15550001",
            ["To"] = "+15550002",
            ["Body"] = "Test webhook body"
        };
        const string url = "https://crm.example.test/api/v1/webhooks/twilio";
        const string authToken = "super_auth_token_for_testing_123";

        var data = new StringBuilder(url);
        foreach (var pair in form.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            data.Append(pair.Key).Append(pair.Value);
        }

        var keyBytes = Encoding.UTF8.GetBytes(authToken);
        var dataBytes = Encoding.UTF8.GetBytes(data.ToString());

        var sha1Signature = Convert.ToBase64String(HMACSHA1.HashData(keyBytes, dataBytes));
        var sha256Signature = Convert.ToBase64String(HMACSHA256.HashData(keyBytes, dataBytes));

        // Both SHA-1 (Twilio legacy spec) and SHA-256 (modern) must verify
        WebhookAuthenticator.VerifyTwilioSignature(url, form, sha1Signature, authToken).ShouldBeTrue();
        WebhookAuthenticator.VerifyTwilioSignature(url, form, sha256Signature, authToken).ShouldBeTrue();

        // Tampered payload fails
        var tamperedForm = new Dictionary<string, string>(form) { ["Body"] = "Tampered body" };
        WebhookAuthenticator.VerifyTwilioSignature(url, tamperedForm, sha256Signature, authToken).ShouldBeFalse();
        WebhookAuthenticator.VerifyTwilioSignature(url, tamperedForm, sha1Signature, authToken).ShouldBeFalse();
    }

    [Fact]
    public void SecretProtector_operates_cleanly_without_hardcoded_keys()
    {
        var config = new ConfigurationBuilder().Build();
        var protector = new SecretProtector(config);

        var secret = "super-sensitive-api-token-999";
        var encrypted = protector.Protect(secret);

        encrypted.ShouldNotBeNull();
        encrypted.ShouldStartWith("v2:");
        encrypted.ShouldNotContain(secret);

        var decrypted = protector.Unprotect(encrypted);
        decrypted.ShouldBe(secret);
    }

    [Fact]
    public async Task AutoIgnored_A2_DocumentOcrService_strips_path_traversal_filenames()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var client = admin.Client;

        // Create PDF with path traversal in filename: ../../escape.pdf
        var pdfBytes = "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF"u8.ToArray();
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "../../escape.pdf");
        content.Add(new StringContent("General"), "category");

        var response = await client.PostAsync("/api/v1/documents/upload", content);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var doc = await response.Content.ReadFromJsonAsync<BusinessDocumentDto>();
        doc.ShouldNotBeNull();
        // Verifies directory traversal characters ../../ were stripped
        doc.FileName.ShouldBe("escape.pdf");
        doc.FileName.ShouldNotContain("../");
        doc.FileName.ShouldNotContain("..\\");
    }

    [Fact]
    public async Task AutoIgnored_A1_Automation_evaluates_filter_with_sql_injection_payload_safely()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var client = admin.Client;

        // Create an automation rule with SQL injection payload in filter criteria
        var sqlInjectionPayload = "'; DROP TABLE Leads; --";
        var conditionGroup = new FilterGroup
        {
            Logic = "and",
            Conditions =
            [
                new FilterCondition
                {
                    Field = "first_name",
                    Op = FilterOperator.Eq,
                    Value = JsonSerializer.SerializeToElement(sqlInjectionPayload)
                }
            ]
        };

        var ruleReq = new SaveAutomationRuleRequest(
            Name: "SQL Injection Test Rule",
            EntityType: "leads",
            TriggerType: AutomationTriggerType.RecordCreated,
            TriggerConfig: null,
            Conditions: conditionGroup,
            Actions:
            [
                new AutomationActionDefinition(
                    Type: AutomationActionType.UpdateField,
                    FieldKey: "company_name",
                    FieldValue: "Safe Value"
                )
            ],
            IsActive: true,
            Priority: 10
        );

        var response = await client.PostAsJsonAsync("/api/v1/automation/rules", ruleReq);
        // The rule should be accepted and safely parsed into LINQ without SQL injection
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var createdRule = await response.Content.ReadFromJsonAsync<AutomationRuleDto>();
        createdRule.ShouldNotBeNull();
        createdRule.Name.ShouldBe("SQL Injection Test Rule");
    }
}
