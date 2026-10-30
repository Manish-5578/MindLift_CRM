$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}

# 1. Create the Book Page in Mindlift Track
$pageBody = @{
    parent = @{ database_id = "eabcafae-aef0-4183-bd4f-1581ac35d947" }
    properties = @{
        Task = @{
            title = @(
                @{
                    type = "text"
                    text = @{ content = "MindLift Features Book" }
                }
            )
        }
        Status = @{
            status = @{ name = "Done" }
        }
        Priority = @{
            select = @{ name = "High" }
        }
        Type = @{
            select = @{ name = "Docs" }
        }
        Area = @{
            multi_select = @(
                @{ name = "Docs" }
            )
        }
    }
} | ConvertTo-Json -Depth 6

$bookPage = Invoke-RestMethod -Uri "https://api.notion.com/v1/pages" -Method Post -Headers $headers -Body $pageBody
$bookPageId = $bookPage.id
Write-Host "Created Book Page: $bookPageId"

# 2. Append rich content blocks to the Book Page
$blocks = @{
    children = @(
        @{
            object = "block"
            type = "heading_1"
            heading_1 = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "MindLift CRM Feature Guide and UI Book" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "paragraph"
            paragraph = @{
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "This book tracks every capability developed for the MindLift CRM platform, detailing its module, business behavior, frontend UI components, and technical workflow." }
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
                        text = @{ content = "Feature #1: Lead Conversion Overhaul" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "callout"
            callout = @{
                icon = @{ emoji = "🎯" }
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Module: Leads & Sales | Key: lead-conversion | Status: UI & Backend Ready" }
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
                        text = @{ content = "Behavior: Seamlessly converts an inbound lead into an Account, a Contact, and an optional Deal in an atomic transaction." }
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
                        text = @{ content = "UI Components: LeadConvertDialog.razor modal dialog launched from the Lead Detail screen (LeadDetail.razor)." }
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
                        text = @{ content = "Conversion Capabilities: Smart account resolution (select existing company or create new), contact deduplication/linking, deal pipeline selection, expected close date, and target deal value." }
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
                        text = @{ content = "Activity Migration: Automatically reparents all lead activities (calls, meetings, tasks) and tags into the newly created contact and account." }
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
                        text = @{ content = "Feature #2: Contact Multi-Account Links" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "callout"
            callout = @{
                icon = @{ emoji = "🏢" }
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Module: Contacts & Accounts | Key: contact-multi-account | Status: UI & Backend Ready" }
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
                        text = @{ content = "Behavior: Allows a single contact to be associated with multiple companies simultaneously (e.g. board member, legal counsel, external consultant, parent/subsidiary advisor)." }
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
                        text = @{ content = "UI Components: ContactDetail.razor (Accounts sidebar card with Primary and Secondary badges, Add Link dialog, and Unlink action) and AccountDetail.razor (Affiliated Contacts list, Affiliate Existing Contact dialog, and Unlink icon)." }
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
                        text = @{ content = "Primary Integrity: Preserves the direct employer on contacts.account_id while storing secondary affiliations with custom company roles in contact_account_links." }
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
                        text = @{ content = "Safe Unlinking: Unlinking only severs the company association link; neither the contact nor the company is deleted." }
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
                        text = @{ content = "Feature #3: Duplicate Detection & Side-by-Side Merge" }
                    }
                )
            }
        },
        @{
            object = "block"
            type = "callout"
            callout = @{
                icon = @{ emoji = "👥" }
                rich_text = @(
                    @{
                        type = "text"
                        text = @{ content = "Module: Leads & Contacts | Key: duplicate-merge | Status: UI & Backend Ready" }
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
                        text = @{ content = "Behavior: Proactively detects duplicate records based on email, phone, and name match criteria, and provides interactive field-by-field consolidation." }
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
                        text = @{ content = "UI Components: SideBySideMergeDialog.razor interactive matrix comparison, Proactive Alert Banners in LeadDetail.razor and ContactDetail.razor, and Checkbox Selection in duplicates card." }
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
                        text = @{ content = "Field Override Selection: Users click on columns/radio buttons to choose which specific field values (names, email, phone, company, status, notes) survive into the Master record." }
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
                        text = @{ content = "Child Reparenting: All logged activities (calls, meetings, tasks), timeline events, deals, and secondary links are seamlessly reparented to the master record. Duplicate records are soft-deleted." }
                    }
                )
            }
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri "https://api.notion.com/v1/blocks/$bookPageId/children" -Method Patch -Headers $headers -Body $blocks
Write-Host "Successfully populated MindLift Features Book with the 3 features!"
