using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Notifications;
using MindLift.Infrastructure.Contacts;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/contacts")]
public sealed class ContactsController(ContactService contacts, ImportNotifier imports) : ControllerBase
{
    [HasPermission(AppModule.Contacts, PermissionAction.View)]
    [HttpPost("query")]
    public async Task<ActionResult<PagedResult<ContactListItemDto>>> Query([FromBody] ListQuery query, CancellationToken cancellationToken)
        => Ok(await contacts.QueryAsync(query, cancellationToken));

    [HasPermission(AppModule.Contacts, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContactDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await contacts.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Contacts, PermissionAction.View)]
    [HttpGet("{id:guid}/summary")]
    public async Task<ActionResult<ContactSummaryDto>> Summary(Guid id, CancellationToken cancellationToken)
        => Ok(await contacts.SummaryAsync(id, cancellationToken));

    [HasPermission(AppModule.Contacts, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<ContactDto>> Create([FromBody] SaveContactRequest request, CancellationToken cancellationToken)
        => Ok(await contacts.CreateAsync(request, cancellationToken));

    [HasPermission(AppModule.Contacts, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContactDto>> Update(Guid id, [FromBody] SaveContactRequest request, CancellationToken cancellationToken)
        => Ok(await contacts.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Contacts, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await contacts.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Change many contacts at once (the list's bulk bar). Each one is checked and saved as if edited on its own page.</summary>
    [HasPermission(AppModule.Contacts, PermissionAction.Edit)]
    [HttpPost("bulk")]
    public async Task<ActionResult<BulkActionResult>> Bulk([FromBody] BulkActionRequest request, CancellationToken cancellationToken)
        => Ok(await contacts.BulkAsync(request, cancellationToken));

    [HasPermission(AppModule.Contacts, PermissionAction.Delete)]
    [HttpPost("bulk-delete")]
    public async Task<ActionResult<BulkActionResult>> BulkDelete([FromBody] BulkDeleteRequest request, CancellationToken cancellationToken)
        => Ok(await contacts.BulkDeleteAsync(request, cancellationToken));

    [HasPermission(AppModule.Contacts, PermissionAction.Create)]
    [HttpPost("check-duplicates")]
    public async Task<ActionResult<IReadOnlyList<DuplicateCandidateDto>>> CheckDuplicates([FromBody] CheckDuplicatesRequest request, CancellationToken cancellationToken)
        => Ok(await contacts.CheckDuplicatesAsync(request, cancellationToken));

    [HasPermission(AppModule.Contacts, PermissionAction.View)]
    [RequiresFeature("duplicate-merge")]
    [HttpGet("{id:guid}/duplicates")]
    public async Task<ActionResult<IReadOnlyList<DuplicateCandidateDto>>> GetDuplicates(Guid id, CancellationToken cancellationToken)
        => Ok(await contacts.GetDuplicatesForContactAsync(id, cancellationToken));

    [HasPermission(AppModule.Contacts, PermissionAction.Delete)]
    [HttpPost("{id:guid}/merge")]
    public async Task<ActionResult<ContactDto>> Merge(Guid id, [FromBody] MergeContactsRequest request, CancellationToken cancellationToken)
        => Ok(await contacts.MergeAsync(id, request.DuplicateIds, request.FieldOverrides, cancellationToken));

    /// <summary>CSV body (text/csv). Max 5,000 rows.</summary>
    [HasPermission(AppModule.Contacts, PermissionAction.Import)]
    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> Import(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var result = await contacts.ImportCsvAsync(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        await imports.NotifyAsync("contacts", "/contacts", result, cancellationToken);
        return Ok(result);
    }

    [HasPermission(AppModule.Contacts, PermissionAction.Export)]
    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ListQuery query, CancellationToken cancellationToken)
    {
        var csv = await contacts.ExportCsvAsync(query, cancellationToken);
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", "contacts.csv");
    }
}

[ApiController]
[Route("api/v1/accounts")]
public sealed class AccountsController(AccountService accounts, ImportNotifier imports) : ControllerBase
{
    [HasPermission(AppModule.Accounts, PermissionAction.View)]
    [HttpPost("query")]
    public async Task<ActionResult<PagedResult<AccountListItemDto>>> Query([FromBody] ListQuery query, CancellationToken cancellationToken)
        => Ok(await accounts.QueryAsync(query, cancellationToken));

    [HasPermission(AppModule.Accounts, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await accounts.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Accounts, PermissionAction.View)]
    [HttpGet("{id:guid}/summary")]
    public async Task<ActionResult<AccountSummaryDto>> Summary(Guid id, CancellationToken cancellationToken)
        => Ok(await accounts.SummaryAsync(id, cancellationToken));

    [HasPermission(AppModule.Accounts, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<AccountDto>> Create([FromBody] SaveAccountRequest request, CancellationToken cancellationToken)
        => Ok(await accounts.CreateAsync(request, cancellationToken));

    [HasPermission(AppModule.Accounts, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AccountDto>> Update(Guid id, [FromBody] SaveAccountRequest request, CancellationToken cancellationToken)
        => Ok(await accounts.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Accounts, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await accounts.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Change many accounts at once (the list's bulk bar). Each one is checked and saved as if edited on its own page.</summary>
    [HasPermission(AppModule.Accounts, PermissionAction.Edit)]
    [HttpPost("bulk")]
    public async Task<ActionResult<BulkActionResult>> Bulk([FromBody] BulkActionRequest request, CancellationToken cancellationToken)
        => Ok(await accounts.BulkAsync(request, cancellationToken));

    [HasPermission(AppModule.Accounts, PermissionAction.Delete)]
    [HttpPost("bulk-delete")]
    public async Task<ActionResult<BulkActionResult>> BulkDelete([FromBody] BulkDeleteRequest request, CancellationToken cancellationToken)
        => Ok(await accounts.BulkDeleteAsync(request, cancellationToken));

    [HasPermission(AppModule.Accounts, PermissionAction.Import)]
    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> Import(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var result = await accounts.ImportCsvAsync(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        await imports.NotifyAsync("accounts", "/accounts", result, cancellationToken);
        return Ok(result);
    }

    [HasPermission(AppModule.Accounts, PermissionAction.Export)]
    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ListQuery query, CancellationToken cancellationToken)
    {
        var csv = await accounts.ExportCsvAsync(query, cancellationToken);
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", "accounts.csv");
    }
}

/// <summary>The search box at the top of every page. Each kind of record is only searched when the person may see it.</summary>
[ApiController]
[Route("api/v1/search")]
[Authorize]
public sealed class SearchController(GlobalSearchService search) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SearchHitDto>>> Get([FromQuery] string? q, CancellationToken cancellationToken)
        => Ok(await search.SearchAsync(q, cancellationToken));
}
