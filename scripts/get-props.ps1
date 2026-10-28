$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
}
$db = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/ba347b03-95f3-468c-be6d-79a4889d5abd" -Headers $headers
foreach ($prop in $db.properties.PSObject.Properties) {
    [PSCustomObject]@{
        Name = $prop.Name
        Type = $prop.Value.type
    }
}
