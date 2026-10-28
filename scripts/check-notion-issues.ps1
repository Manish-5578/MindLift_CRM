$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}
$issuesDbId = "ba347b03-95f3-468c-be6d-79a4889d5abd"
$res = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/$issuesDbId/query" -Method Post -Headers $headers -Body '{"page_size":100}'

$allIssues = @()
$notFixed = @()

foreach ($item in $res.results) {
    $title = if ($item.properties.Issue.title.Count -gt 0) { $item.properties.Issue.title[0].plain_text.Trim() } else { "Untitled" }
    $status = if ($item.properties.Status.status) { $item.properties.Status.status.name } else { "Unknown" }
    $severity = if ($item.properties.Severity.select) { $item.properties.Severity.select.name } else { "None" }
    $area = if ($item.properties.Area.multi_select) { ($item.properties.Area.multi_select | ForEach-Object { $_.name }) -join ", " } else { "" }
    
    $obj = [PSCustomObject]@{
        Title = $title
        Status = $status
        Severity = $severity
        Area = $area
    }
    $allIssues += $obj
    if ($status -ne "Fixed") {
        $notFixed += $obj
    }
}

Write-Host "Total issues in Mindlift CRM Issues: $($allIssues.Count)"
Write-Host "Non-Fixed issues: $($notFixed.Count)"
if ($notFixed.Count -gt 0) {
    Write-Host "Listing non-fixed issues:"
    $notFixed | Format-Table -AutoSize
} else {
    Write-Host "All issues in Notion CRM Issues are marked as Fixed!"
}

Write-Host "`nAll Issues Status Summary:"
$allIssues | Group-Object Status | Select-Object Name, Count | Format-Table -AutoSize
