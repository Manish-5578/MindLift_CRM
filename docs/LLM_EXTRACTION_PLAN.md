# Plan: AI-assisted field extraction with fields you define

Status: built on 2026-10-23 (all eight steps, local model only, no cloud option). What was built and how it was tested is in `PROGRESS.md`, section "Fields you define, read by AI". The decisions in section 7 were: download the 7B model, local only, reading in the background is fine, all document types.

## 1. Problem

The text Tesseract (or the PDF's own text layer) produces is mostly good. The weak part is turning that text into fields.
Today that step is a set of fixed patterns for four document types (trade licence, Emirates ID, invoice, receipt) plus per-type patterns a
user can write by hand (`document_type_definitions`). On the three real files uploaded so far, each produced **one** field ("Total"),
including a CV, where it is meaningless. A passport appointment receipt has a dozen useful values and none were found.

## 2. Goal

- In **Settings > Document types** (the "document section") a user adds, edits and removes the fields wanted from each kind of document.
  Describing a field in plain words ("the applicant's date of birth") is enough; no pattern writing.
- When a document is read, Tesseract supplies the text and a language model fills in exactly those fields.
- Every value is checked against the document text before it is trusted, and a person still reviews and approves (the review screen exists).
- It works with a model on the customer's own machine, and optionally a cloud model. Without any model, today's behaviour stays.

## 3. What is on this computer (checked 2026-10-23)

| Item | Finding |
|---|---|
| Ollama | Installed (0.40.1) and running on `localhost:11434` |
| Models installed | `qwen2.5-coder:1.5b` (a coding model, 1 GB), `nomic-embed-text` and `qwen3-embedding:8b` (search embeddings; they cannot read or extract) |
| Hardware | Intel i5-13420H, 8 cores, 15.7 GB RAM, **no usable GPU** (Intel UHD only), so models run on the CPU |
| Disk | **C: has 2 GB free**; D: has 109 GB. Ollama keeps models on C: by default, so `OLLAMA_MODELS` must point to D: before any pull |

Measured test: `qwen2.5-coder:1.5b`, passport receipt text (834 tokens in), 17 seconds including model load, 26 tokens/s out.
It returned 4 of 5 fields exactly right (given name, surname, date of birth, application number) and merged the time into the appointment date.
So local extraction works, and a proper instruction model should do better.

Recommended model to add: **`qwen2.5:7b-instruct`** (about 4.7 GB). Not measured here: I expect roughly a minute per document on this CPU
(estimate, not a measurement), so reading must not block the upload screen (see step 2). `qwen2.5:3b-instruct` is a faster fallback.
Licence check before shipping: as I recall, Qwen2.5 1.5B, 7B and 14B are Apache 2.0, while 3B and 72B carry Qwen's own licence with
commercial limits. Verify that on the model pages.

## 4. Design

### 4.1 Field definitions (extends what exists)
`DocumentFieldRuleDto` is stored as JSON on the document type, so new optional properties need no database migration.

| New property | Meaning |
|---|---|
| `Description` | What to look for, in words. This is what the model reads. |
| `Example` | An example value (helps the model and the test screen). |
| `AllowedValues` | Optional fixed list, for fields such as Gender or Marital status. |
| `Mode` | `Auto` (AI when on, else rules), `Rules` (anchor or pattern only), `Ai` (AI only). |

`Anchor` and `Pattern` become optional. Value types: Text, Id, Number, Amount, Date, plus Phone and Email.

### 4.2 Reading pipeline
1. Read the file: PDF text layer or OCR (unchanged).
2. Pick the document type (keyword score today; later also an AI choice among the workspace's active types).
3. If AI is enabled for the workspace and the type has fields: build a prompt from the field list and the page text.
   Temperature 0. The output is forced into a JSON schema built from the fields (Ollama structured output; tool use or JSON mode for cloud).
4. **Check each value against the text.** Normalise whitespace, case, digits, dates and amounts (the app already has normalisers), then require the
   value to appear in the document text. Found exactly: high confidence. Found after normalising: medium. Not found: kept, marked
   "not found in the document, please check", never auto-approved.
5. Validate by type (the date parses, the amount parses, the value is in `AllowedValues`).
6. Store per field: value, confidence, page, the matching snippet, and how it was found (AI or rules).
7. If the model is unreachable, times out, or returns invalid JSON twice: use the rules for that type, record why, and never leave the document stuck.

Long documents: send only the pages most likely to hold the fields (first pages, plus pages with the type's keywords), within a character budget
(`Llm__MaxInputChars`), instead of all 50 pages.

### 4.3 Providers behind one interface (`ILlmClient`)
| Provider | Use | Notes |
|---|---|---|
| Ollama | Local, default for sensitive documents | Health check shows whether it is reachable and the model is installed |
| OpenAI-compatible endpoint | LM Studio, llama.cpp server, vLLM | Same code path as Ollama with a different URL |
| Anthropic API | Optional cloud, best accuracy | API key supplied by the customer, stored encrypted like channel secrets |

### 4.4 Privacy and safety
- AI reading is **off per workspace by default**. A separate switch allows a cloud provider, and an extra rule can keep identity documents local-only.
- The document text is sent only to the provider the admin chose. Text is not written to logs. Each AI read is recorded in the audit log (document, provider, model).
- Documents can contain instructions aimed at the model. They are treated as data: the output is schema-constrained, the model has no tools,
  and the grounding check in 4.2 rejects anything not in the text.
- Reading runs one document at a time on a CPU model (reuse the existing reader gate) with a timeout.

## 5. Build order

| Step | What | Size | Done when |
|---|---|---|---|
| 1 | Field model: `Description`, `Example`, `AllowedValues`, `Mode`; optional pattern; "Add field" form on Document types; extend the existing **Test on a sample** panel to show value, source snippet and method per field | 2 days | A user adds "Applicant name" in words and sees it on a sample |
| 2 | **Background reading.** Upload returns at once; a Hangfire job reads the document; status Queued, Reading, Ready; the screen updates. This also removes the "stuck at pending" failure | 2 days | Upload with a slow model never blocks and never sticks |
| 3 | `ILlmClient` with Ollama and OpenAI-compatible providers, prompt and schema builder, JSON validation, retry, fallback to rules, settings and a health check page | 3 days | The sample passport receipt returns correct fields from local Ollama |
| 4 | Grounding check, type validation, confidence, provenance (page and snippet) saved with the result | 2 days | A made-up value is flagged, never trusted |
| 5 | Review screen: show every defined field (empty ones too, editable), highlight the source, "Add field to this type" and "Re-read" buttons | 2 days | A missing field can be added in review and filled by re-reading |
| 6 | "Suggest fields from a sample document" on Document types | 1 day | Upload a sample, get a proposed field list to edit |
| 7 | Anthropic provider, encrypted key, workspace consent switches, audit entries | 2 days | Cloud works only when explicitly allowed |
| 8 | Benchmark: the three real files plus synthetic ones, exact-match accuracy per model (1.5B, 3B, 7B, cloud); tests with a fake model (deterministic); one opt-in test against live Ollama | 2 days | Accuracy table in `PROGRESS.md`; regressions caught by tests |

About 16 working days in all. Steps 1 to 4 give the working feature; 5 to 8 make it dependable and safe.

## 6. Also fix on the way (not AI)
- Two-column pages mix lines from both columns (visible in the passport receipt). Try Tesseract page-layout mode 4 or a column split before the AI step.
- English-only pages pick up stray Arabic glyphs because the reader runs `eng+ara` together. Detect the script first, or read English-only first and add Arabic only when Arabic letters appear.
- Remove the rule that treats any unrecognised document as an invoice, which produced the meaningless "Total: 91.00" on a CV.

## 7. Decisions needed from the owner
1. Download `qwen2.5:7b-instruct` (about 4.7 GB) to D:? (Moves Ollama's model folder to D: first, because C: is nearly full.)
2. Offer a cloud option (Anthropic API key) at all, or local only?
3. Is "Reading..." for up to a minute after upload acceptable?
4. Which document types first? The passport receipt, CV and KEA receipt are the real samples so far.
