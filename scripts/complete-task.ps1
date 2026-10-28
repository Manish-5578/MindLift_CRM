param(
    [string]$TaskId = "4070c080-692b-81cc-862c-d1dd1306f280",
    [string]$Status = "Done"
)

$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type"  = "application/json"
}

$body = @{
    "properties" = @{
        "Status" = @{
            "status" = @{ "name" = $Status }
        }
    }
} | ConvertTo-Json -Depth 5

$url = "https://api.notion.com/v1/pages/$TaskId"
Write-Host "Updating Notion page $TaskId to $Status..."
$response = Invoke-RestMethod -Uri $url -Method Patch -Headers $headers -Body $body
Write-Host "Success! Status is now: $($response.properties.Status.status.name)"
