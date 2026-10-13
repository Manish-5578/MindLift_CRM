using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

// ================= PRODUCTS CONTROLLER =================

[ApiController]
[Authorize]
[Route("api/v1/products")]
public sealed class ProductsController(ICpqService cpqService) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Products, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> List([FromQuery] bool activeOnly = false, CancellationToken ct = default)
    {
        var list = await cpqService.GetProductsAsync(activeOnly, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(AppModule.Products, PermissionAction.View)]
    public async Task<ActionResult<ProductDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var prod = await cpqService.GetProductByIdAsync(id, ct);
        if (prod == null) return NotFound();
        return Ok(prod);
    }

    [HttpPost]
    [HasPermission(AppModule.Products, PermissionAction.Create)]
    public async Task<ActionResult<ProductDto>> Create([FromBody] SaveProductRequest request, CancellationToken ct = default)
    {
        var created = await cpqService.CreateProductAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(AppModule.Products, PermissionAction.Edit)]
    public async Task<ActionResult<ProductDto>> Update(Guid id, [FromBody] SaveProductRequest request, CancellationToken ct = default)
    {
        var updated = await cpqService.UpdateProductAsync(id, request, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(AppModule.Products, PermissionAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var deleted = await cpqService.DeleteProductAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }
}

// ================= PRICE BOOKS CONTROLLER =================

[ApiController]
[Authorize]
[Route("api/v1/price-books")]
public sealed class PriceBooksController(ICpqService cpqService) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Products, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<PriceBookDto>>> List(CancellationToken ct = default)
    {
        var list = await cpqService.GetPriceBooksAsync(ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(AppModule.Products, PermissionAction.View)]
    public async Task<ActionResult<PriceBookDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var pb = await cpqService.GetPriceBookByIdAsync(id, ct);
        if (pb == null) return NotFound();
        return Ok(pb);
    }

    [HttpPost]
    [HasPermission(AppModule.Products, PermissionAction.Create)]
    public async Task<ActionResult<PriceBookDto>> Create([FromBody] SavePriceBookRequest request, CancellationToken ct = default)
    {
        var created = await cpqService.CreatePriceBookAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/entries")]
    [HasPermission(AppModule.Products, PermissionAction.Edit)]
    public async Task<ActionResult<PriceBookEntryDto>> SetEntry(Guid id, [FromBody] SavePriceBookEntryRequest request, CancellationToken ct = default)
    {
        var entry = await cpqService.SetPriceBookEntryAsync(id, request, ct);
        return Ok(entry);
    }
}

// ================= QUOTES CONTROLLER =================

[ApiController]
[Authorize]
[Route("api/v1/quotes")]
public sealed class QuotesController(ICpqService cpqService) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Quotations, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<QuoteDto>>> List([FromQuery] Guid? dealId, [FromQuery] Guid? accountId, CancellationToken ct = default)
    {
        var list = await cpqService.GetQuotesAsync(dealId, accountId, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(AppModule.Quotations, PermissionAction.View)]
    public async Task<ActionResult<QuoteDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var quote = await cpqService.GetQuoteByIdAsync(id, ct);
        if (quote == null) return NotFound();
        return Ok(quote);
    }

    [HttpPost]
    [HasPermission(AppModule.Quotations, PermissionAction.Create)]
    public async Task<ActionResult<QuoteDto>> Create([FromBody] SaveQuoteRequest request, CancellationToken ct = default)
    {
        var created = await cpqService.CreateQuoteAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(AppModule.Quotations, PermissionAction.Edit)]
    public async Task<ActionResult<QuoteDto>> Update(Guid id, [FromBody] SaveQuoteRequest request, CancellationToken ct = default)
    {
        var updated = await cpqService.UpdateQuoteAsync(id, request, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(AppModule.Quotations, PermissionAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var deleted = await cpqService.DeleteQuoteAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/sync")]
    [HasPermission(AppModule.Quotations, PermissionAction.Edit)]
    public async Task<ActionResult<QuoteDto>> SyncToDeal(Guid id, CancellationToken ct = default)
    {
        var synced = await cpqService.SyncQuoteToDealAsync(id, ct);
        return Ok(synced);
    }

    [HttpPost("{id:guid}/convert-to-invoice")]
    [HasPermission(AppModule.Invoices, PermissionAction.Create)]
    public async Task<ActionResult<InvoiceDto>> ConvertToInvoice(Guid id, CancellationToken ct = default)
    {
        var inv = await cpqService.ConvertQuoteToInvoiceAsync(id, ct);
        return Ok(inv);
    }

    [HttpPost("{id:guid}/status")]
    [HasPermission(AppModule.Quotations, PermissionAction.Edit)]
    public async Task<ActionResult<QuoteDto>> UpdateStatus(Guid id, [FromQuery] QuoteStatus status, CancellationToken ct = default)
    {
        var updated = await cpqService.UpdateQuoteStatusAsync(id, status, ct);
        return Ok(updated);
    }
}

// ================= INVOICES CONTROLLER =================

[ApiController]
[Authorize]
[Route("api/v1/invoices")]
public sealed class InvoicesController(ICpqService cpqService) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Invoices, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> List([FromQuery] Guid? dealId, [FromQuery] Guid? accountId, CancellationToken ct = default)
    {
        var list = await cpqService.GetInvoicesAsync(dealId, accountId, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(AppModule.Invoices, PermissionAction.View)]
    public async Task<ActionResult<InvoiceDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var inv = await cpqService.GetInvoiceByIdAsync(id, ct);
        if (inv == null) return NotFound();
        return Ok(inv);
    }

    [HttpPost]
    [HasPermission(AppModule.Invoices, PermissionAction.Create)]
    public async Task<ActionResult<InvoiceDto>> Create([FromBody] SaveInvoiceRequest request, CancellationToken ct = default)
    {
        var created = await cpqService.CreateInvoiceAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/payments")]
    [HasPermission(AppModule.Invoices, PermissionAction.Edit)]
    public async Task<ActionResult<PaymentDto>> RecordPayment(Guid id, [FromBody] RecordPaymentRequest request, CancellationToken ct = default)
    {
        var payment = await cpqService.RecordPaymentAsync(id, request, ct);
        return Ok(payment);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(AppModule.Invoices, PermissionAction.Edit)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct = default)
    {
        var cancelled = await cpqService.CancelInvoiceAsync(id, ct);
        if (!cancelled) return NotFound();
        return NoContent();
    }
}

// ================= DOCUMENTS & OCR CONTROLLER =================

[ApiController]
[Authorize]
[Route("api/v1/documents")]
public sealed class DocumentsController(IDocumentOcrService ocrService) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Documents, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<BusinessDocumentDto>>> List([FromQuery] Guid? accountId, [FromQuery] Guid? dealId, CancellationToken ct = default)
    {
        var docs = await ocrService.GetDocumentsAsync(accountId, dealId, ct);
        return Ok(docs);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(AppModule.Documents, PermissionAction.View)]
    public async Task<ActionResult<BusinessDocumentDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var doc = await ocrService.GetDocumentByIdAsync(id, ct);
        if (doc == null) return NotFound();
        return Ok(doc);
    }

    [HttpGet("{id:guid}/file")]
    [HasPermission(AppModule.Documents, PermissionAction.View)]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken ct = default)
    {
        var result = await ocrService.GetDocumentStreamAsync(id, ct);
        if (result == null) return NotFound();
        return File(result.Value.Stream, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("upload")]
    [HasPermission(AppModule.Documents, PermissionAction.Create)]
    public async Task<ActionResult<BusinessDocumentDto>> Upload(
        [FromForm] IFormFile file,
        [FromForm] string category = "General",
        [FromForm] Guid? accountId = null,
        [FromForm] Guid? dealId = null,
        [FromForm] Guid? quoteId = null,
        [FromForm] Guid? invoiceId = null,
        [FromForm] Guid? contactId = null,
        [FromForm] bool sharedWithPortal = false,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0) return BadRequest("File is empty.");
        if (file.Length > 25 * 1024 * 1024) return BadRequest("File exceeds maximum allowed size of 25 MB.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        string[] blockedExtensions = [".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".vbs", ".msi", ".scr", ".jar"];
        if (blockedExtensions.Contains(ext))
        {
            return BadRequest($"File extension '{ext}' is prohibited for security reasons.");
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var doc = await ocrService.UploadDocumentAsync(
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            file.Length,
            bytes,
            category,
            accountId,
            dealId,
            quoteId,
            invoiceId,
            contactId,
            sharedWithPortal,
            ct);

        return CreatedAtAction(nameof(GetById), new { id = doc.Id }, doc);
    }

    [HttpPost("{id:guid}/ocr")]
    [HasPermission(AppModule.Documents, PermissionAction.Edit)]
    public async Task<ActionResult<OcrResultDto>> ProcessOcr(Guid id, CancellationToken ct = default)
    {
        var result = await ocrService.ProcessOcrAsync(id, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/review")]
    [HasPermission(AppModule.Documents, PermissionAction.Edit)]
    public async Task<ActionResult<BusinessDocumentDto>> Review(Guid id, [FromBody] ReviewDocumentRequest request, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var result = await ocrService.ReviewDocumentAsync(id, request, userId, ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/fields")]
    [HasPermission(AppModule.Documents, PermissionAction.Edit)]
    public async Task<IActionResult> UpdateFields(Guid id, [FromBody] UpdateExtractedFieldsRequest request, CancellationToken ct = default)
    {
        var success = await ocrService.UpdateExtractedFieldsAsync(id, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/reprocess")]
    [HasPermission(AppModule.Documents, PermissionAction.Edit)]
    public async Task<ActionResult<BusinessDocumentDto>> Reprocess(Guid id, CancellationToken ct = default)
    {
        var doc = await ocrService.ReprocessAsync(id, ct);
        return Ok(doc);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(AppModule.Documents, PermissionAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var deleted = await ocrService.DeleteDocumentAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    private Guid GetUserId()
    {
        var val = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(val, out var id) ? id : Guid.Empty;
    }
}
