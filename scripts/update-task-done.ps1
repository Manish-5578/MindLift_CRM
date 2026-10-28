$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}
$trackDbId = "eabcafae-aef0-4183-bd4f-1581ac35d947"
$res = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/$trackDbId/query" -Method Post -Headers $headers -Body '{"page_size":100}'

foreach ($item in $res.results) {
    if ($item.properties.Task.title.Count -gt 0) {
        $title = $item.properties.Task.title[0].plain_text.Trim()
        if ($title -like "*Restart the app on the merged code*") {
            Write-Host "Marking task as Done: $title ($($item.id))"
            $body = @{
                "properties" = @{
                    "Status" = @{
                        "status" = @{ "name" = "Done" }
                    }
                }
            } | ConvertTo-Json -Depth 5
            Invoke-RestMethod -Uri "https://api.notion.com/v1/pages/$($item.id)" -Method Patch -Headers $headers -Body $body | Out-Null
            Write-Host "Task updated successfully!"
        }
    }
}
