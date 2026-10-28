param(
    [string]$Title,
    [string]$Area = "Deals",
    [string]$Type = "Build",
    [string]$Priority = "High",
    [string]$Status = "In progress"
)

$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type"  = "application/json"
}

$body = @{
    parent = @{ database_id = "eabcafae-aef0-4183-bd4f-1581ac35d947" }
    properties = @{
        Task = @{
            title = @(
                @{
                    type = "text"
                    text = @{ content = $Title }
                }
            )
        }
        Status = @{
            status = @{ name = $Status }
        }
        Priority = @{
            select = @{ name = $Priority }
        }
        Type = @{
            select = @{ name = $Type }
        }
        Area = @{
            multi_select = @(
                @{ name = $Area }
            )
        }
    }
} | ConvertTo-Json -Depth 6

$res = Invoke-RestMethod -Uri "https://api.notion.com/v1/pages" -Method Post -Headers $headers -Body $body
Write-Host "Created task: $($res.id) | $($Title)"
return $res.id
