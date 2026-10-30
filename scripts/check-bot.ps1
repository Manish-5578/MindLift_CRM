$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
}
$bot = Invoke-RestMethod -Uri "https://api.notion.com/v1/users/me" -Headers $headers
$bot | ConvertTo-Json -Depth 5
