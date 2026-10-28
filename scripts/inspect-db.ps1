$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type"  = "application/json"
}

$url = "https://api.notion.com/v1/databases/eabcafae-aef0-4183-bd4f-1581ac35d947/query"
$res = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body '{"page_size": 5}'
$first = $res.results[0]
$first.properties.PSObject.Properties | ForEach-Object {
    [PSCustomObject]@{
        PropertyName = $_.Name
        Type = $_.Value.type
    }
} | Format-Table -AutoSize
