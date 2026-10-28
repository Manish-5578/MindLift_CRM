$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type"  = "application/json"
}

$url = "https://api.notion.com/v1/databases/eabcafae-aef0-4183-bd4f-1581ac35d947/query"
$res = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body '{"page_size": 100}'
$res.results | ForEach-Object {
    $task = ""
    if ($_.properties.Task.title.Count -gt 0) {
        $task = $_.properties.Task.title[0].plain_text
    }
    $status = ""
    if ($_.properties.Status.status) {
        $status = $_.properties.Status.status.name
    }
    $area = ($_.properties.Area.multi_select | ForEach-Object { $_.name }) -join ", "
    [PSCustomObject]@{
        Id     = $_.id
        Status = $status
        Area   = $area
        Task   = $task
    }
} | Format-Table -AutoSize
