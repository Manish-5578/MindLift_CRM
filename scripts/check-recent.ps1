$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}
$res = Invoke-RestMethod -Uri "https://api.notion.com/v1/search" -Method Post -Headers $headers -Body '{"sort":{"direction":"descending","timestamp":"last_edited_time"},"page_size":10}'
$res.results | ForEach-Object {
    [PSCustomObject]@{
        object = $_.object
        id = $_.id
        parent_type = $_.parent.type
        title = if ($_.title) { $_.title[0].plain_text } elseif ($_.properties.title) { $_.properties.title.title[0].plain_text } elseif ($_.properties.Name) { $_.properties.Name.title[0].plain_text } elseif ($_.properties.Task) { $_.properties.Task.title[0].plain_text } elseif ($_.properties.Feature) { $_.properties.Feature.title[0].plain_text } else { 'no-title' }
        url = $_.url
    }
} | Format-Table -AutoSize
