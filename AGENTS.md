# MindLift CRM Agent Rules & Workflow

## Notion Tracking & Issue Management

Always synchronize project activities and findings with the connected Notion workspace:

### Connected Databases
- **Mindlift Track (Roadmap & Tasks):** `eabcafae-aef0-4183-bd4f-1581ac35d947`
- **Mindlift CRM Issues (Defects & Security Findings):** `ba347b03-95f3-468c-be6d-79a4889d5abd`
- **MindLift Features (Capabilities & Metrics Tracker):** `4080c080-692b-8150-9c69-f5f9cd788cc7`
- **Integration Token:** `ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds`
- **API Endpoint:** `https://api.notion.com/v1/` (Header `Notion-Version: 2022-06-28`)

### Workflow Protocol
1. **Log Every Issue in Mindlift CRM Issues:**
   - Whenever an issue, bug, or security vulnerability is discovered, investigated, or resolved, log it in the `Mindlift CRM Issues` database.
   - Record `Issue` (title), `Severity` (Critical, High, Medium, Low), `Area`, `Found during`, `Found on`, `Fixed on`, `Status` (Open, Investigating, Fixed), `What happened`, and `Cause and fix`.

2. **Track Every Task in Mindlift Track:**
   - Whenever any build, test, fix, refactor, or architectural decision is undertaken, track it in the `Mindlift Track` database.
   - Maintain accurate `Status` (Not started, In progress, Needs testing, Done), `Priority` (High, Medium, Low), `Type` (Build, Test, Fix, Decision, Docs, Ops), `Area`, and `Notes`.
