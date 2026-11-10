# Business Card OCR in the Mobile App

How a business card photo becomes a lead in MindLift CRM: the screens, the technology, the logic, the limits and the tests.
Written from the code as it stands (November 2026). File paths are relative to the repository root.

---

## 1. Summary

- Staff open **Scan Business Card** from the **+** quick-add menu or the Leads screen, photograph (or pick) the **front** of a card, and optionally the **back**.
- The phone sends the images to the **server**. The server reads the text (OCR), works out company, name, job title, phone, email, website and address, scores how sure it is of each field, checks the CRM for duplicates and returns the result.
- If the server cannot be reached, the phone reads the text itself with **Google ML Kit** (Latin script only) and a simpler rule set fills the form.
- The person reviews the form. Anything doubtful is flagged and must be approved before saving. Saving creates a **Lead** with source "Mobile Business Card Scan". If the phone is offline, the lead waits in a durable outbox and is sent later.

OCR for **documents** (trade licences, passports, invoices) is a related but separate feature that shares the same server OCR engine. It is covered briefly in section 9.

---

## 2. Technology stack

| Layer | Technology | Purpose |
|---|---|---|
| Mobile app | Flutter / Dart, Riverpod, Dio | UI, state, HTTP |
| Photo capture | `image_picker` ^1.1.2 | Camera and gallery |
| On-device OCR (offline fallback) | `google_mlkit_text_recognition` ^0.15.0 (Google ML Kit, Latin model) | Reads text without a network |
| Mobile UI parts | `mindlift_ui` (MlSheet, MlToast, MlButton...) | Custom components only, per the rule book |
| Offline storage | `offlineDatabaseProvider` outbox (`LocalOutboxMutation`) | Holds leads created without a network |
| API client | `mindlift_api` package (`ChatService.scanCardSides`, `parseLeadCard`) | Talks to the server |
| Server API | ASP.NET Core (.NET 10) | `LeadOcrController` |
| Primary OCR engine | **Tesseract 5** (command-line, LSTM mode `--oem 1`), languages `eng+ara` | Reads text and word boxes |
| Second OCR engine | **PaddleOCR PP-OCRv5** models through `RapidOcrNet` 4.2.0 on ONNX Runtime | Backup for weak Latin-script pages |
| Image cleaning | **SkiaSharp** 4.153 plus the app's own `ImageEnhancer` | Rotate, straighten, contrast, black-and-white |
| Parsing | `LeadCardOcrService`, `CardLineClassifier` (C#) | Turns text into fields |
| Database | EF Core and PostgreSQL (duplicate lookups only; nothing about the scan is stored) | Duplicate check |

Server images: the Dockerfile installs `tesseract-ocr`, `tesseract-ocr-eng` and `tesseract-ocr-ara`; `Ocr__TesseractPath=/usr/bin/tesseract` and `Ocr__Languages=eng+ara` are set in the Dockerfile and `docker-compose.prod.yml`. The PaddleOCR models ship inside the RapidOcrNet package and are copied to `models/v5` beside the app, so nothing is downloaded at run time.

---

## 3. End-to-end flow

```
Phone                                   Server
-----                                   ------
1. Capture front (and back)
   image_picker, max 3200x3200, quality 98
2. Size sanity check (<15 KB warns)
3. POST /api/v1/leads/scan-card-file ->  4. Stream each image to a temp file
   multipart: "front", optional "back"   5. Check file signature (JPEG/PNG/WEBP/TIFF)
                                         6. OCR pipeline per side (section 5)
                                         7. Parse text into fields (section 6)
                                         8. Score confidence per field
                                         9. Duplicate check in the CRM
                                        10. Merge front + back (section 7)
11. Fill the form  <---------------------- JSON result (ExtractedLeadCardDto)
12. Review gate: low confidence? conflicts?
13. Person fixes / approves, taps Save
14. POST create lead  (or outbox if offline)
```

Fallback when step 3 fails (no network, server error): the phone runs ML Kit on each image, joins the text and runs its local parser (section 8). A third entry point, `POST /api/v1/leads/parse-card`, accepts text (or a base64 image) and is used when the person edits or pastes card text and taps parse.

---

## 4. Mobile app in detail

**Files**
- `mobile/packages/mindlift_mobile/lib/features/leads/presentation/ocr_lead_scan_sheet.dart` - the whole sheet (about 1,250 lines).
- `.../card_line_classifier.dart` - phone copy of the address/name rules.
- `mobile/packages/mindlift_api/lib/src/services/chat_service.dart` - `scanCardSides`, `parseLeadCard`.
- `mobile/packages/mindlift_api/lib/src/models/chat_models.dart` - `ExtractedLeadCardDto` and related models.
- Entry points: `leads_screen.dart` (scan button) and `quick_add_sheet.dart` ("Scan Business Card with OCR").
- Arabic strings: `lib/core/l10n.dart`.

**Capture**
- Camera or gallery, front slot required, back slot optional ("Capture the front of the card first" if the back is added first).
- Images are limited to 3200 x 3200 and quality 98 by `image_picker` so they are sharp but not huge.
- A file under 15 KB shows a "very small or dark" warning but is still processed.
- Adding, replacing or removing a side clears the earlier result and rescans.

**Permissions**
- Android: `CAMERA`, `READ_EXTERNAL_STORAGE`, `INTERNET`; the manifest also declares `com.google.mlkit.vision.DEPENDENCIES = ocr` so the ML Kit text model is downloaded when the app is installed.
- iOS: `NSCameraUsageDescription` and `NSPhotoLibraryUsageDescription` in `Info.plist`.

**The form**
Company, contact person, phone, email, job title, address, plus the raw text. A side panel shows detected script (Latin / Arabic / Bilingual) and overall confidence. A potential duplicate is shown with its record type (Lead, Contact, Account) and name.

**Review gate** (`_refreshReviewGate`)
Saving is blocked until the person approves when any of these is true:
- a field that has a value has confidence **below 0.75** (company, name, phone, email, title, address);
- the front and back disagree on a field (a conflict);
- the contact person looks like an address (digits or a colon in a name, or the address classifier says so).

Low-confidence words in a logo or tagline do not block a card because only fields that have a value are checked. When review is needed and the server returned word boxes, a **Review OCR** view draws each word box over the front image (colour by confidence) so the person can see what was read. Conflicts show both readings as chips; tapping one chooses it. A "Contact person looks like an address" warning offers a one-tap move into the address field.

**Saving** (`_handleSaveLead`)
- Company or name is required. Name is never an address.
- Notes record the job title, that a low-confidence read was confirmed by staff, and that a duplicate warning was overridden (with the matched record and ID) so there is an audit trail.
- Source is set to `Mobile Business Card Scan`.
- Network failure (connection error or timeout): the lead is written to the local outbox with a temporary ID and synced later; the person is told it is queued.
- Server errors keep the sheet open so nothing typed is lost: 403 permission, 409 duplicate, 400 validation message, 5xx retry message.

---

## 5. Server OCR pipeline

**Endpoint** `POST /api/v1/leads/scan-card-file` in `src/MindLift.Api/Controllers/LeadOcrController.cs`
- Requires sign-in and the **Leads: Create** permission. Rate limited by the `uploads` policy, counted per company.
- Accepts a raw image body, or multipart with a `front` part and an optional `back` part (more than two images is refused). Request limit 32 MB.
- Parts are streamed straight to a temporary file (no large in-memory buffer) and the file is deleted afterwards.
- The first bytes must match a **JPEG, PNG, WEBP or TIFF** signature; anything else is refused.

**Reading a page** (`src/MindLift.Infrastructure/Documents/Text/OcrPagePipeline.cs`, `DocumentPageReader.cs`, `ImageEnhancer.cs`)
1. **Prepare**: apply the camera's rotation, turn the page upright if Tesseract's orientation report is confident, estimate and correct skew (within about 10 degrees), convert to gray, stretch contrast (ignoring the extreme 1% at each end), enlarge small pictures. A blank page is detected and read once without cleaning.
2. **Read** with Tesseract using several looks at the page, cheapest first, stopping as soon as the score reaches the target (85): enhanced, original, denoised, restored, black-and-white, then different page-layout modes. At most 6 attempts per page.
3. **Rescue**: if the best score is below 55 (reads like noise) the page may be on its side, so the other quarter turns are tried. If the page looks mostly Arabic and still scores low, it is also read with the Arabic model alone (mixed Arabic and English can leak Latin letters into Arabic words).
4. **Second engine**: if the score is still under 70, PaddleOCR PP-OCRv5 reads the enhanced page, only when no Arabic or other non-Latin letters were found (its models are Latin only). Its result is kept only if it scores better.
5. **Score**: each word counts by length; words with no letter or digit (noise from specks and borders) lower the score by up to half. The best reading wins. Below 70 the page is flagged low confidence.
6. Word boxes are mapped back to the **original photo** as fractions (0 to 1), so the phone can draw them at any size.

Limits come from `OcrOptions` (`src/MindLift.Infrastructure/Cpq/OcrEngine.cs`): 300 DPI, 60 seconds per page, 25 MB file, 60 million pixels, 25 million pixels for cleaning, 2 documents read at once, one Tesseract thread per run. Tesseract output is capped at 20 MB and the process is stopped if it overruns. Settings can be overridden with `Ocr__*` environment variables.

---

## 6. Turning text into fields

`LeadCardOcrService.ParseTextAndCheckDuplicatesAsync` (`src/MindLift.Infrastructure/Leads/LeadCardOcrService.cs`), with `CardLineClassifier.cs` beside it. Arabic-Indic digits are converted to ASCII first. Each line is tested in this order, and the first rule that claims it wins:

1. **Email** (regex).
2. **Website** (`http(s)://` or `www.`).
3. **Address**: landmark or street words (street, road, tower, floor, P.O. Box and Arabic equivalents), an Indian PIN code such as "560 001", an `Address:` label, or a line made only of place names. A line with a company word (LLC, Ltd, Trading...) stays a company unless it has clear address-only evidence. This runs **before** phone matching so a PIN code is not mistaken for a phone number.
4. **Phone**: a number of 7 or more digits. UAE formats are normalised, for example 0501234567 becomes +971 50 123 4567.
5. **Job title**: keyword list in English and Arabic (director, manager, CEO, مدير عام...).
6. **Company**: keyword list including Arabic legal forms (ذ.م.م, ش.م.ح, شركة...).
7. Leftover lines: the first that can be a person's name (2 to 5 word-like tokens, no digits or symbols, nothing address-like) becomes the **name**; otherwise a short line (up to 6 words) becomes the company if none was found.
8. If there is still no name but there is an email like `zaid.mansoor@...`, a name is derived from it (confidence 0.50).

Script is reported as Latin, Arabic or Bilingual from the characters present.

**Confidence per field**

| Field | Score |
|---|---|
| Email | 0.95 |
| Phone | 0.90 |
| Job title | 0.88 |
| Company | 0.90 with a company keyword, otherwise 0.70 |
| Name | from `NameConfidence`: higher when it matches the email or carries an honorific (Mr, Dr, السيد...), lower for a leftover line, 0.50 if taken from the email |
| Address | from `AddressConfidence`: a PIN code or several address words are solid, one weak hint is not |
| Overall | average of the field scores, blended 50/50 with the OCR engine's own page confidence |

**Duplicate check** (`FindDuplicateInCrmAsync`): by email (leads then contacts), by the last 7 digits of the phone, then by exact company name (accounts then leads). The first hit is returned with its record type. Deleted records are ignored. Failures are logged and ignored so a database problem never blocks a scan.

---

## 7. Front and back merge

`MergeFrontAndBackAsync`: for each field the side with higher confidence wins (front on a tie). Values that match after ignoring formatting (case, spaces, phone punctuation, `www.`) count as one. When both sides have different values, the winner is returned and both readings go in `conflicts` so the person chooses. Addresses from both sides are combined without repeating a piece. Word boxes are tagged `front` or `back`. A back with little on it (one field or fewer) does not lower the overall score. The duplicate check is repeated on the merged values because the back may carry the email that finds the match.

---

## 8. Offline behaviour on the phone

When the server call fails, `_scanSides` runs ML Kit (`TextRecognitionScript.latin`) on each side and `_extractLocally` parses the text with the same ideas in simpler form: email regex, address-first rule using the phone copy of `CardLineClassifier`, phone with 7 or more digits, keyword lists for title and company, then name and company from leftovers.

Because the phone's guess is weaker, it is **never presented as certain**: overall confidence is 0.75, name and address are set to 0.6 (below the 0.75 gate), so a locally read card always asks for review. Company, phone, email and title carry no score on the phone, so they are not gated.

Known limit: the ML Kit model used is **Latin only**. An Arabic card read offline will give little or no text, and the person is asked to enter details or retry when online.

The classifier is kept in step with the server copy by design ("Mirrors the server's CardLineClassifier"), but it is a hand-maintained duplicate in Dart, so the two lists can drift.

---

## 9. Related: document OCR (not the card scanner)

The same Tesseract and PaddleOCR engines read uploaded documents (`DocumentOcrService`, `DocumentReadJob`, `DocumentPageReader`): a PDF page with a real text layer is read directly with no OCR, scanned pages are rendered and read, and passport machine-readable zones are parsed by `MrzParser`. Dates, amounts and text are cleaned by the normalisers in `src/MindLift.Application/Common/Ocr`. A searchable-PDF writer (`SearchablePdfService`) and an OCR health check (`OcrHealthCheck`) also exist. The mobile app does not run this flow itself.

---

## 10. Security and privacy

- Authenticated and permission-checked; rate limited per company.
- Files are validated by their first bytes, size-limited, and deleted from the server's temp folder after reading.
- The card image and text are **not stored** by the scanner; only the lead the person saves is.
- Duplicate lookups run inside the signed-in company only.
- Duplicate overrides and staff review confirmations are recorded in the lead's notes.

---

## 11. Tests

- `tests/MindLift.UnitTests/ChatAndOcr/CardClassificationTests.cs` and `ChatAndOcrTests.cs` - card line classification and parsing.
- `mobile/packages/mindlift_mobile/test/card_line_classifier_test.dart` - the phone copy of the classifier (6 tests).
- `tests/MindLift.IntegrationTests/Documents/` - `OcrPipelineTests`, `OcrAccuracyTests`, `ArabicOcrTests`, `SecondOcrEngineTests`, `OcrLimitsTests`, `SearchablePdfTests` - engine behaviour with fixture images.
- Not covered by any automated test that I found: the full mobile scan sheet on a device, the offline ML Kit fallback, and the front/back merge through the HTTP endpoint with a real photo.

---

## 12. Known limits and suggestions

Implemented limits:
- Offline reading is Latin only.
- Name, company and title detection is rule and keyword based. Cards with unusual layouts, stylised logos or names that look like places can be misread; the review gate exists for this.
- Address rules favour Indian and Gulf formats (PIN codes, UAE phone normalisation, city lists).
- Only the first page of each image is read; only two images (front and back) are accepted.
- Keyword and place lists exist in both Dart and C#.

Suggestions (not implemented):
- Add a device test and an endpoint test for the front/back flow.
- Share the classifier word lists from one source to stop the two copies drifting.
- Add an Arabic-capable offline option, or tell the person clearly when offline Arabic is unsupported.
- Add vCard/QR detection for cards that carry one.
