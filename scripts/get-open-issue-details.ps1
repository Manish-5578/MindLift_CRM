$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}
$issuesDbId = "ba347b03-95f3-468c-be6d-79a4889d5abd"
$res = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/$issuesDbId/query" -Method Post -Headers $headers -Body '{"page_size":100}'

foreach ($item in $res.results) {
    $status = if ($item.properties.Status.status) { $item.properties.Status.status.name } else { "" }
    if ($status -eq "Open" -or $status -eq "Investigating") {
        $title = $item.properties.Issue.title[0].plain_text.Trim()
        $severity = $item.properties.Severity.select.name
        $area = ($item.properties.Area.multi_select | ForEach-Object { $_.name }) -join ", "
        $what = if ($item.properties."What happened".rich_text.Count -gt 0) { $item.properties."What happened".rich_text[0].plain_text } else { "" }
        $cause = if ($item.properties."Cause and fix".rich_text.Count -gt 0) { $item.properties."Cause and fix".rich_text[0].plain_text } else { "" }
        
        Write-Host "=========================================="
        Write-Host "Title: $title"
        Write-Host "Severity: $severity | Area: $area | Status: $status"
        Write-Host "What happened: $what"
        Write-Host "Cause and fix: $cause"
        Write-Host ""
    }
}
