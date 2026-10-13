using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Cpq;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Cpq;

public sealed class DocumentReviewAndOcrTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static MultipartFormDataContent CreateFileContent(string content, string fileName)
    {
        var form = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(content);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", fileName);
        return form;
    }


    [Fact]
    public async Task A_file_that_cannot_be_read_is_marked_failed_and_no_values_are_invented()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var garbage = new MultipartFormDataContent();
        // A valid PDF header followed by unreadable content: it passes the upload check and fails at OCR.
        var bytes = new ByteArrayContent("%PDF-1.4\n"u8.ToArray().Concat(new byte[] { 0x00, 0x13, 0x37, 0xFF, 0x01, 0x02, 0x03 }).ToArray());
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        garbage.Add(bytes, "file", "broken-scan.pdf");
        var uploadRes = await admin.Client.PostAsync("/api/v1/documents/upload", garbage);
        uploadRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var doc = (await uploadRes.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;

        var ocr = (await (await admin.Client.PostAsync($"/api/v1/documents/{doc.Id}/ocr", null)).Content.ReadFromJsonAsync<OcrResultDto>())!;

        ocr.Status.ShouldBe(DocumentOcrStatus.Failed);
        ocr.ExtractedFields.ShouldBeEmpty();
        ocr.DetectedVendor.ShouldBeNull();
        ocr.DetectedTotal.ShouldBeNull();
        ocr.DetectedInvoiceNumber.ShouldBeNull();
        var stored = await admin.Client.GetFromJsonAsync<BusinessDocumentDto>($"/api/v1/documents/{doc.Id}");
        stored!.Status.ShouldBe("Failed");
    }
    [Fact]
    public async Task Upload_And_Process_Passport_Mrz_Extracts_Structured_Data()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "documents", "synthetic_passport_mrz.txt");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures", "documents", "synthetic_passport_mrz.txt");
        }

        var mrzContent = File.Exists(fixturePath)
            ? await File.ReadAllTextAsync(fixturePath)
            : "P<GBRSMITH<<JOHN<ARTHUR<<<<<<<<<<<<<<<<<<<<<\n9876543213GBR8501019M2704151<<<<<<<<<<<<<<00";

        var form = CreateFileContent(mrzContent, "passport.txt");
        var uploadRes = await admin.Client.PostAsync("/api/v1/documents/upload", form);
        uploadRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var doc = (await uploadRes.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;

        var ocrRes = await admin.Client.PostAsync($"/api/v1/documents/{doc.Id}/ocr", null);
        ocrRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ocr = (await ocrRes.Content.ReadFromJsonAsync<OcrResultDto>())!;

        ocr.DocumentType.ShouldBe("Passport");
        ocr.DetectedInvoiceNumber.ShouldBe("987654321");
        ocr.DetectedVendor.ShouldNotBeNull();
        ocr.DetectedVendor.ShouldContain("SMITH JOHN ARTHUR");

        var updatedDoc = await admin.Client.GetFromJsonAsync<BusinessDocumentDto>($"/api/v1/documents/{doc.Id}");
        updatedDoc!.Status.ShouldBe("NeedsReview");
        updatedDoc.ExpiryDate.ShouldBe(new DateOnly(2027, 4, 15));
    }

    [Fact]
    public async Task Upload_And_Process_Arabic_Trade_License_Extracts_Dates_And_Digits()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "documents", "synthetic_trade_license_ar.txt");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures", "documents", "synthetic_trade_license_ar.txt");
        }

        var arContent = File.Exists(fixturePath)
            ? await File.ReadAllTextAsync(fixturePath)
            : "رخصة تجارية\nالاسم التجاري: شركة الصقر الذهبي\nرقم الرخصة: CN-982145\nتاريخ الانتهاء: ١٤/٠٢/٢٠٢٧";

        var form = CreateFileContent(arContent, "trade_license_ar.txt");
        var uploadRes = await admin.Client.PostAsync("/api/v1/documents/upload", form);
        uploadRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var doc = (await uploadRes.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;

        var ocrRes = await admin.Client.PostAsync($"/api/v1/documents/{doc.Id}/ocr", null);
        ocrRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ocr = (await ocrRes.Content.ReadFromJsonAsync<OcrResultDto>())!;

        ocr.DocumentType.ShouldBe("TradeLicense");
        ocr.DetectedInvoiceNumber.ShouldBe("CN-982145");

        var updatedDoc = await admin.Client.GetFromJsonAsync<BusinessDocumentDto>($"/api/v1/documents/{doc.Id}");
        updatedDoc!.ExpiryDate.ShouldBe(new DateOnly(2027, 2, 14));
    }

    [Fact]
    public async Task Review_Document_Approve_And_Apply_To_Account()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var accRes = await admin.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest(
            "Apex Middle East Holdings", null, null, null, null, null, null, null));
        accRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var account = (await accRes.Content.ReadFromJsonAsync<AccountDto>())!;

        var form = CreateFileContent("License No: CN-9901\nCompany Name: Apex Middle East Holdings", "license.txt");
        form.Add(new StringContent(account.Id.ToString()), "accountId");
        var uploadRes = await admin.Client.PostAsync("/api/v1/documents/upload", form);
        var doc = (await uploadRes.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;

        // Approve and apply fields
        var reviewReq = new ReviewDocumentRequest("Approve", null,
        [
            new ApplyFieldToRecordItem("LicenseNumber", "accounts.trade_license_number", "CN-9901")
        ]);

        var reviewRes = await admin.Client.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/review", reviewReq);
        reviewRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var approvedDoc = await admin.Client.GetFromJsonAsync<BusinessDocumentDto>($"/api/v1/documents/{doc.Id}");
        approvedDoc!.Status.ShouldBe("Approved");
        approvedDoc.ReviewedById.ShouldNotBeNull();
        approvedDoc.ReviewedAt.ShouldNotBeNull();

        // Verify account updated
        var updatedAccount = await admin.Client.GetFromJsonAsync<AccountDto>($"/api/v1/accounts/{account.Id}");
        updatedAccount!.Description.ShouldNotBeNull();
        updatedAccount.Description.ShouldContain("[License: CN-9901]");
    }

    [Fact]
    public async Task Review_Document_Reject_With_Reason()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var form = CreateFileContent("Some document text", "doc.txt");
        var uploadRes = await admin.Client.PostAsync("/api/v1/documents/upload", form);
        var doc = (await uploadRes.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;

        var rejectReq = new ReviewDocumentRequest("Reject", "Unreadable scan quality");
        var reviewRes = await admin.Client.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/review", rejectReq);
        reviewRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rejectedDoc = await admin.Client.GetFromJsonAsync<BusinessDocumentDto>($"/api/v1/documents/{doc.Id}");
        rejectedDoc!.Status.ShouldBe("Rejected");
        rejectedDoc.RejectReason.ShouldBe("Unreadable scan quality");
    }

    [Fact]
    public async Task Document_Expiry_Job_Dispatches_Alert_For_Impending_Expiry()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Upload and approve a document expiring in 20 days (within 30 days)
        var form = CreateFileContent("Trade License", "expiring_license.txt");
        var uploadRes = await admin.Client.PostAsync("/api/v1/documents/upload", form);
        var doc = (await uploadRes.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var targetExpiry = today.AddDays(20);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var entity = await db.Set<BusinessDocument>().IgnoreQueryFilters().FirstAsync(d => d.Id == doc.Id);
            using (tContext.Use(entity.TenantId))
            {
                entity.Status = "Approved";
                entity.ExpiryDate = targetExpiry;
                await db.SaveChangesAsync();
            }
        }

        // Run expiry job
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<DocumentExpiryJob>();
            await job.RunAsync(CancellationToken.None);
        }

        // Verify offset 30 is recorded
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Set<BusinessDocument>().IgnoreQueryFilters().FirstAsync(d => d.Id == doc.Id);
            entity.ExpiryNotifiedOffsetsJson.ShouldNotBeNull();
            entity.ExpiryNotifiedOffsetsJson.ShouldContain("30");
        }
    }

    [Fact]
    public async Task Ocr_Health_Check_Returns_Success_On_Health_Endpoint()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/health");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await res.Content.ReadAsStringAsync();
        content.ShouldBeOneOf("Healthy", "Degraded");
    }

    [Fact]
    public async Task Document_Tenant_Isolation_And_Permissions_Enforced()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        var form = CreateFileContent("Confidential Tenant A Document", "tenantA.txt");
        var uploadRes = await tenantA.Client.PostAsync("/api/v1/documents/upload", form);
        var docA = (await uploadRes.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;

        // Tenant B cannot access Tenant A document
        var bAccess = await tenantB.Client.GetAsync($"/api/v1/documents/{docA.Id}");
        bAccess.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B cannot review Tenant A document
        var bReview = await tenantB.Client.PostAsJsonAsync($"/api/v1/documents/{docA.Id}/review", new ReviewDocumentRequest("Approve"));
        bReview.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // User without Documents.Edit cannot review
        var roleName = $"NoDocs {Guid.NewGuid():N}";
        var created = await tenantA.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest(roleName, null, DashboardView.Staff));
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var noDocsUser = await Workspaces.InviteAndAcceptAsync(factory, tenantA, roleName, "no-docs");
        var deniedReview = await noDocsUser.Client.PostAsJsonAsync($"/api/v1/documents/{docA.Id}/review", new ReviewDocumentRequest("Approve"));
        deniedReview.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Upload_Rejects_Prohibited_File_Extensions()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var badForm = CreateFileContent("malicious payload", "malware.exe");
        var res = await admin.Client.PostAsync("/api/v1/documents/upload", badForm);
        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadAsStringAsync();
        body.ShouldContain("prohibited for security reasons");
    }
}

