$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}
$trackDbId = "eabcafae-aef0-4183-bd4f-1581ac35d947"
$res = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/$trackDbId/query" -Method Post -Headers $headers -Body '{"page_size":100}'

$allTasks = @()
$notDone = @()

foreach ($item in $res.results) {
    $title = if ($item.properties.Task.title.Count -gt 0) { $item.properties.Task.title[0].plain_text.Trim() } else { "Untitled" }
    $status = if ($item.properties.Status.status) { $item.properties.Status.status.name } else { "Unknown" }
    $priority = if ($item.properties.Priority.select) { $item.properties.Priority.select.name } else { "None" }
    $type = if ($item.properties.Type.select) { $item.properties.Type.select.name } else { "None" }
    $area = if ($item.properties.Area.multi_select) { ($item.properties.Area.multi_select | ForEach-Object { $_.name }) -join ", " } else { "" }
    
    $obj = [PSCustomObject]@{
        Title = $title
        Status = $status
        Priority = $priority
        Type = $type
        Area = $area
    }
    $allTasks += $obj
    if ($status -ne "Done") {
        $notDone += $obj
    }
}

Write-Host "Total tasks in Mindlift Track: $($allTasks.Count)"
Write-Host "Non-Done tasks: $($notDone.Count)"
if ($notDone.Count -gt 0) {
    Write-Host "Listing non-done tasks:"
    $notDone | Format-Table -AutoSize
} else {
    Write-Host "All tasks in Mindlift Track are marked as Done!"
}

Write-Host "`nAll Tasks Status Summary:"
$allTasks | Group-Object Status | Select-Object Name, Count | Format-Table -AutoSize
