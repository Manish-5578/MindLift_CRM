$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}

$pageId = "4080c080-692b-8116-af46-f1bebbb6ce54"

# Try move page to workspace
try {
    $body = @{ parent = @{ workspace = $true } } | ConvertTo-Json
    $res = Invoke-RestMethod -Uri "https://api.notion.com/v1/pages/$pageId" -Method Patch -Headers $headers -Body $body
    Write-Host "Success moving page!"
} catch {
    Write-Host "Move error: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host "Server response: $($reader.ReadToEnd())"
    }
}
