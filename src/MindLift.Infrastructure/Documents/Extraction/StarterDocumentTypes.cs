using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Documents.Extraction;

/// <summary>
/// Ready-made document types an administrator can add with one click and then change: the words that identify each kind of document and the fields worth taking
/// from it, each described in plain words for the AI reader. Names of people are saved to a contact's first and last name; nothing else is saved to a record
/// until the administrator chooses a target (for example a custom "Date of birth" field on contacts).
/// </summary>
public static class StarterDocumentTypes
{
    private static DocumentFieldRuleDto F(string key, string label, string type, string description, string? example = null, bool required = false, bool metric = false,
        string? saveTo = null, string? anchor = null, IReadOnlyList<string>? allowed = null)
        => new(key, label, type, anchor, null, required, metric, description, example, allowed, DocumentFieldModes.Auto, saveTo);

    private static DocumentKeywordDto K(string text, int weight) => new(text, weight);

    public static readonly IReadOnlyList<SaveDocumentTypeRequest> All =
    [
        new("invoice", "Invoice", "A bill from a supplier asking for payment", true, 3,
            [K("INVOICE", 3), K("Tax Invoice", 3), K("Invoice No", 2), K("Invoice Number", 2), K("Bill To", 1), K("Due Date", 1), K("VAT", 1)],
            [
                F("vendor_name", "Vendor", DocumentValueTypes.Text, "The company that issued the invoice, usually at the top", "Gulf Trading LLC", true),
                F("invoice_number", "Invoice number", DocumentValueTypes.Id, "The invoice's own number or reference", "INV-2026-00418", true),
                F("invoice_date", "Invoice date", DocumentValueTypes.Date, "The date the invoice was issued"),
                F("due_date", "Due date", DocumentValueTypes.Date, "The date payment is due"),
                F("currency", "Currency", DocumentValueTypes.Text, "The currency the amounts are in, as a code or symbol", "AED"),
                F("tax_amount", "Tax amount", DocumentValueTypes.Amount, "The VAT or tax amount charged"),
                F("total_amount", "Total", DocumentValueTypes.Amount, "The total amount to pay, including tax", "12,450.75", true, true),
            ]),

        new("receipt", "Receipt", "Proof of a payment made", true, 3,
            [K("RECEIPT", 3), K("Payment Receipt", 3), K("Amount Paid", 2), K("Paid", 1), K("Transaction", 1), K("Payment Details", 2)],
            [
                F("merchant", "Paid to", DocumentValueTypes.Text, "Who the payment was made to or who issued the receipt"),
                F("receipt_number", "Receipt number", DocumentValueTypes.Id, "The receipt, transaction or reference number"),
                F("payment_date", "Payment date", DocumentValueTypes.Date, "The date the payment was made"),
                F("amount_paid", "Amount paid", DocumentValueTypes.Amount, "The amount that was paid", null, true, true),
                F("payment_method", "Payment method", DocumentValueTypes.Text, "How it was paid, such as card, cash or bank transfer"),
            ]),

        new("trade_licence", "Trade licence", "A company's licence to trade", true, 3,
            [K("TRADE LICENSE", 3), K("TRADE LICENCE", 3), K("Commercial License", 3), K("License No", 2), K("Licence No", 2), K("Licensing Authority", 1), K("رخصة تجارية", 3)],
            [
                F("licence_number", "Licence number", DocumentValueTypes.Id, "The licence number", "CN-1234567", true),
                F("company_name", "Company name", DocumentValueTypes.Text, "The trade name or company the licence was issued to", null, true),
                F("legal_form", "Legal form", DocumentValueTypes.Text, "The legal form of the company, such as LLC or sole establishment"),
                F("issue_date", "Issue date", DocumentValueTypes.Date, "The date the licence was issued"),
                F("expiry_date", "Expiry date", DocumentValueTypes.Date, "The date the licence expires", null, true),
            ]),

        new("emirates_id", "Emirates ID", "UAE national identity card", true, 3,
            [K("Emirates ID", 3), K("Identity Card", 2), K("United Arab Emirates", 1), K("784-", 2), K("هوية", 2)],
            [
                F("id_number", "ID number", DocumentValueTypes.Id, "The 15 digit identity number that starts with 784", "784-1990-1234567-1", true),
                F("full_name", "Full name", DocumentValueTypes.Text, "The cardholder's full name as printed", null, true),
                F("nationality", "Nationality", DocumentValueTypes.Text, "The cardholder's nationality"),
                F("date_of_birth", "Date of birth", DocumentValueTypes.Date, "The cardholder's date of birth"),
                F("expiry_date", "Expiry date", DocumentValueTypes.Date, "The date the card expires", null, true),
            ]),

        new("passport", "Passport", "The identity page of a passport", true, 3,
            [K("PASSPORT", 3), K("Passport No", 2), K("Date of Expiry", 2), K("Nationality", 1), K("P<", 3)],
            [
                F("passport_number", "Passport number", DocumentValueTypes.Id, "The passport number", null, true),
                F("surname", "Surname", DocumentValueTypes.Text, "The holder's surname or family name", null, true, saveTo: "contacts.last_name"),
                F("given_names", "Given names", DocumentValueTypes.Text, "The holder's given or first names", null, true, saveTo: "contacts.first_name"),
                F("nationality", "Nationality", DocumentValueTypes.Text, "The holder's nationality"),
                F("date_of_birth", "Date of birth", DocumentValueTypes.Date, "The holder's date of birth"),
                F("sex", "Sex", DocumentValueTypes.Text, "The holder's sex as printed", null, false, allowed: ["Male", "Female", "M", "F"]),
                F("expiry_date", "Expiry date", DocumentValueTypes.Date, "The date the passport expires", null, true),
            ]),

        new("appointment_receipt", "Appointment receipt", "A confirmation slip for an appointment, such as a passport service appointment", true, 3,
            [K("Appointment", 2), K("Application Reference", 2), K("Appointment Receipt", 3), K("Reporting Date", 1), K("Passport Seva", 3)],
            [
                F("application_reference_no", "Application reference number", DocumentValueTypes.Id, "The application reference number (ARN) or file number", "26-1060599710", true),
                F("given_name", "Given name", DocumentValueTypes.Text, "The applicant's given or first name", null, true, saveTo: "contacts.first_name"),
                F("surname", "Surname", DocumentValueTypes.Text, "The applicant's surname or family name", null, true, saveTo: "contacts.last_name"),
                F("gender", "Gender", DocumentValueTypes.Text, "The applicant's gender", null, false, allowed: ["Male", "Female", "Other"]),
                F("date_of_birth", "Date of birth", DocumentValueTypes.Date, "The applicant's date of birth"),
                F("place_of_birth", "Place of birth", DocumentValueTypes.Text, "The applicant's place of birth"),
                F("appointment_date", "Appointment date", DocumentValueTypes.Date, "The date of the appointment"),
                F("appointment_time", "Appointment time", DocumentValueTypes.Text, "The time of the appointment, for example 03:00 PM"),
                F("total_fee", "Total fee", DocumentValueTypes.Amount, "The total fee for the service", null, false, true),
            ]),

        new("resume", "CV / Resume", "A person's curriculum vitae", true, 3,
            [K("Curriculum Vitae", 3), K("Resume", 3), K("Work Experience", 2), K("Education", 1), K("Skills", 1), K("Professional Summary", 2), K("Employment History", 2)],
            [
                F("first_name", "First name", DocumentValueTypes.Text, "The person's first or given name", null, true, saveTo: "contacts.first_name"),
                F("last_name", "Last name", DocumentValueTypes.Text, "The person's last or family name", null, true, saveTo: "contacts.last_name"),
                F("email", "Email", DocumentValueTypes.Email, "The person's email address", null, false, saveTo: "contacts.email"),
                F("phone", "Phone", DocumentValueTypes.Phone, "The person's phone number", null, false, saveTo: "contacts.phone"),
                F("job_title", "Current job title", DocumentValueTypes.Text, "The most recent or current job title", null, false, saveTo: "contacts.job_title"),
                F("current_employer", "Current employer", DocumentValueTypes.Text, "The most recent or current employer"),
                F("location", "Location", DocumentValueTypes.Text, "The city or country the person lives in"),
            ]),

        new("contract", "Contract", "An agreement between two or more parties", true, 3,
            [K("Agreement", 2), K("Contract", 2), K("Parties", 1), K("Terms and Conditions", 1), K("Governing Law", 2), K("hereinafter", 2), K("Effective Date", 2)],
            [
                F("party_one", "First party", DocumentValueTypes.Text, "The first party to the agreement"),
                F("party_two", "Second party", DocumentValueTypes.Text, "The second party to the agreement"),
                F("effective_date", "Effective date", DocumentValueTypes.Date, "The date the agreement starts"),
                F("end_date", "End date", DocumentValueTypes.Date, "The date the agreement ends or expires"),
                F("contract_value", "Contract value", DocumentValueTypes.Amount, "The total value or price of the agreement", null, false, true),
                F("governing_law", "Governing law", DocumentValueTypes.Text, "The country or jurisdiction whose law governs the agreement"),
            ]),
    ];
}
