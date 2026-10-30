$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}

$dbId = "4080c080-692b-8150-9c69-f5f9cd788cc7"

# Try move database to workspace root
try {
    $body = @{ parent = @{ workspace = $true } } | ConvertTo-Json
    $res = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/$dbId" -Method Patch -Headers $headers -Body $body
    Write-Host "Success moving database to workspace root!"
} catch {
    Write-Host "Move DB error: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host "Server response: $($reader.ReadToEnd())"
    }
}
