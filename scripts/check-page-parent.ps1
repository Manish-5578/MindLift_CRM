$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
}
$page = Invoke-RestMethod -Uri "https://api.notion.com/v1/pages/4080c080-692b-8116-af46-f1bebbb6ce54" -Headers $headers
Write-Host "Page Parent Type: $($page.parent.type)"
Write-Host "Page Parent Workspace: $($page.parent.workspace)"
Write-Host "Page Parent DB ID: $($page.parent.database_id)"
Write-Host "Page URL: $($page.url)"
