$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}
$issuesQuery = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/ba347b03-95f3-468c-be6d-79a4889d5abd/query" -Method Post -Headers $headers -Body '{"page_size":100}'
foreach ($r in $issuesQuery.results) {
    $title = if ($r.properties.Issue.title.Count -gt 0) { $r.properties.Issue.title[0].plain_text } else { "No Title" }
    $status = if ($r.properties.Status.status) { $r.properties.Status.status.name } else { "No Status" }
    Write-Host "$($r.id) | $status | $title"
}
