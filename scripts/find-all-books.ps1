$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}

$hasMore = $true
$startCursor = $null
$allDatabases = @()
$allPages = @()

while ($hasMore) {
    $body = @{ page_size = 100 }
    if ($startCursor) { $body["start_cursor"] = $startCursor }
    $json = $body | ConvertTo-Json
    $res = Invoke-RestMethod -Uri "https://api.notion.com/v1/search" -Method Post -Headers $headers -Body $json
    foreach ($item in $res.results) {
        if ($item.object -eq "database") {
            $allDatabases += $item
        } elseif ($item.parent.type -ne "database_id") {
            $allPages += $item
        }
    }
    $hasMore = $res.has_more
    $startCursor = $res.next_cursor
}

Write-Host "=== All Databases Found ($($allDatabases.Count)) ==="
$allDatabases | ForEach-Object {
    [PSCustomObject]@{
        id = $_.id
        title = if ($_.title -and $_.title.Count -gt 0) { $_.title[0].plain_text } else { 'no-title' }
        parent_type = $_.parent.type
        parent_id = if ($_.parent.workspace) { "workspace" } elseif ($_.parent.page_id) { $_.parent.page_id } else { $_.parent.database_id }
        url = $_.url
    }
} | Format-Table -AutoSize

Write-Host "=== Non-Database Pages ($($allPages.Count)) ==="
$allPages | ForEach-Object {
    [PSCustomObject]@{
        id = $_.id
        title = if ($_.properties.title) { $_.properties.title.title[0].plain_text } else { 'no-title' }
        parent_type = $_.parent.type
        url = $_.url
    }
} | Format-Table -AutoSize
