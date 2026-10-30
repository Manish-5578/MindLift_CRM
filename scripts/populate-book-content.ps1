$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}

$pageId = "4080c080-692b-8116-af46-f1bebbb6ce54"

$featureBlocks = @{
    children = @(
        @{
            object = "block"
            type = "divider"
            divider = @{}
        },
        @{
            object = "block"
            type = "heading_2"
            heading_2 = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Feature 1: Lead Conversion Overhaul" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "quote"
            quote = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Module: Leads & Sales | Feature Key: lead-conversion | Status: UI & Backend Ready" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Business Behaviour: Converts qualified leads into an Account, a Contact, and an optional Deal in an atomic transaction." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "UI Implementation: Built with LeadConvertDialog.razor modal dialog launched directly from the Lead Detail screen (LeadDetail.razor)." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Capabilities: Smart company matching (select existing or create new account), automatic contact creation and deduplication, deal pipeline selection with stage, amount, and close date." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "History & Data Preservation: Seamlessly migrates all past calls, tasks, meetings, timeline events, and custom tags into the converted contact and account." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "divider"
            divider = @{}
        },
        @{
            object = "block"
            type = "heading_2"
            heading_2 = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Feature 2: Contact Multi-Account Links" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "quote"
            quote = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Module: Contacts & Accounts | Feature Key: contact-multi-account | Status: UI & Backend Ready" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Business Behaviour: Enables associating a single contact with multiple companies simultaneously (e.g. external legal counsel, board director, advisor, partner)." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "UI Implementation: Fully bidirectional UI across ContactDetail.razor (Accounts sidebar card with Primary & Secondary badges, Add Link dialog, and Unlink action) and AccountDetail.razor (Affiliated Contacts list, Affiliate Existing Contact dialog, and Unlink action)." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Data Integrity: The contact's primary employer is maintained on contacts.account_id, while secondary affiliations are stored with custom roles in contact_account_links." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Safe Unlinking: Unlinking only severs the company affiliation; neither the contact nor the company record is removed." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "divider"
            divider = @{}
        },
        @{
            object = "block"
            type = "heading_2"
            heading_2 = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Feature 3: Proactive Duplicate Detection & Side-by-Side Merge" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "quote"
            quote = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Module: Leads & Contacts | Feature Key: duplicate-merge | Status: UI & Backend Ready" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Business Behaviour: Automatically scans and detects potential duplicate records based on email, phone, and name match criteria, and provides interactive field-by-field consolidation." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "UI Implementation: Built with SideBySideMergeDialog.razor comparison matrix, proactive warning alert banners on LeadDetail.razor and ContactDetail.razor, and candidate selection panel." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Field Override Selection: Users visually compare records in columns (Master vs Duplicates) and click to choose which specific field values survive into the final record." }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "bulleted_list_item"
            bulleted_list_item = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Zero-Loss Child Reparenting: All logged activities (calls, meetings, tasks), timeline events, deals, and secondary links belonging to the duplicates are reparented to the master record. Duplicate records are safely soft-deleted." }
                    }
                )
            }
        }
    )
} | ConvertTo-Json -Depth 10

try {
    $res = Invoke-RestMethod -Uri "https://api.notion.com/v1/blocks/$pageId/children" -Method Patch -Headers $headers -Body $featureBlocks
    Write-Host "Success adding all feature sections! Added: $($res.results.Count) blocks"
} catch {
    Write-Host "Error: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host "Server response: $($reader.ReadToEnd())"
    }
}
