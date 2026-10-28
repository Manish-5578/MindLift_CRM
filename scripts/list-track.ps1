$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}
$trackQuery = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/eabcafae-aef0-4183-bd4f-1581ac35d947/query" -Method Post -Headers $headers -Body '{"page_size":100}'
foreach ($r in $trackQuery.results) {
    $taskProp = $r.properties.Task.title
    $title = if ($taskProp.Count -gt 0) { $taskProp[0].plain_text } else { "No Title" }
    $status = if ($r.properties.Status.status) { $r.properties.Status.status.name } else { "No Status" }
    Write-Host "$($r.id) | $status | $title"
}
