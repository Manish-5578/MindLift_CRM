using Microsoft.JSInterop;
using MindLift.UI.Localization;

namespace MindLift.Portal.Services;

/// <summary>
/// English and Arabic text for the customer portal. Kept in one place so every screen can switch language,
/// and so pages never put string literals inside Razor attribute expressions.
/// </summary>
public sealed class PortalText(IJSRuntime js, Translator translator)
{
    public string Language { get; private set; } = "en";
    public bool IsArabic => Language == "ar";
    public event Action? Changed;

    public string this[string key] =>
        Strings.TryGetValue(key, out var pair) ? (IsArabic ? pair.Ar : pair.En) : key;

    public async Task SetLanguageAsync(string language)
    {
        Language = language == "ar" ? "ar" : "en";
        translator.Choose(Language);
        try
        {
            await js.InvokeVoidAsync("mindliftPortal.setLanguage", Language);
        }
        catch (JSException)
        {
            // The page still re-renders in the new language.
        }

        Changed?.Invoke();
    }

    public string Status(string status) => this["status." + status];

    private static readonly Dictionary<string, (string En, string Ar)> Strings = new()
    {
        ["app.name"] = ("Customer portal", "بوابة العملاء"),
        ["nav.home"] = ("Home", "الرئيسية"),
        ["nav.documents"] = ("Documents", "المستندات"),
        ["nav.requests"] = ("Requests", "الطلبات"),
        ["nav.profile"] = ("Profile", "الملف الشخصي"),
        ["nav.signout"] = ("Sign out", "تسجيل الخروج"),
        ["lang.toggle"] = ("العربية", "English"),

        ["login.title"] = ("Sign in", "تسجيل الدخول"),
        ["login.subtitle"] = ("Use the email address your account manager invited.", "استخدم البريد الإلكتروني الذي دعاك إليه مدير حسابك."),
        ["login.email"] = ("Email", "البريد الإلكتروني"),
        ["login.password"] = ("Password", "كلمة المرور"),
        ["login.submit"] = ("Sign in", "دخول"),
        ["login.magic"] = ("Email me a sign-in link instead", "أرسل لي رابط دخول بالبريد"),
        ["login.usePassword"] = ("Use my password instead", "استخدم كلمة المرور"),
        ["login.sendLink"] = ("Send sign-in link", "إرسال رابط الدخول"),
        ["login.linkSent"] = ("If that email has portal access, a sign-in link is on its way. It works for 15 minutes.", "إذا كان لهذا البريد صلاحية دخول، فسيصلك رابط خلال لحظات. صالح لمدة 15 دقيقة."),
        ["login.failed"] = ("That email and password do not match.", "البريد الإلكتروني وكلمة المرور غير متطابقين."),
        ["login.disabled"] = ("This customer portal is switched off. Contact your account manager.", "بوابة العملاء متوقفة حالياً. تواصل مع مدير حسابك."),
        ["login.unknownCompany"] = ("We could not find this company's portal. Use the link from your email.", "تعذر العثور على بوابة هذه الشركة. استخدم الرابط الموجود في بريدك."),

        ["magic.checking"] = ("Signing you in…", "جارٍ تسجيل دخولك…"),
        ["magic.prompt"] = ("Click below to complete sign-in to your portal.", "انقر أدناه لإكمال تسجيل الدخول إلى بوابتك."),
        ["magic.confirm"] = ("Sign In to Portal", "الدخول إلى البوابة"),
        ["magic.failed"] = ("This sign-in link has expired or was already used. Ask for a new one.", "انتهت صلاحية رابط الدخول أو تم استخدامه. اطلب رابطاً جديداً."),
        ["magic.back"] = ("Back to sign in", "العودة لتسجيل الدخول"),

        ["invite.title"] = ("Set up your portal access", "إعداد دخولك إلى البوابة"),
        ["invite.subtitle"] = ("Choose a password. You can also sign in with an email link later.", "اختر كلمة مرور. يمكنك أيضاً الدخول لاحقاً عبر رابط بالبريد."),
        ["invite.password"] = ("New password", "كلمة المرور الجديدة"),
        ["invite.confirm"] = ("Confirm password", "تأكيد كلمة المرور"),
        ["invite.submit"] = ("Activate my access", "تفعيل الدخول"),
        ["invite.mismatch"] = ("The two passwords are different.", "كلمتا المرور غير متطابقتين."),
        ["invite.short"] = ("Use at least 8 characters.", "استخدم 8 أحرف على الأقل."),
        ["invite.failed"] = ("This invitation has expired or was already used. Ask your account manager for a new one.", "انتهت صلاحية الدعوة أو تم استخدامها. اطلب دعوة جديدة من مدير حسابك."),
        ["invite.missing"] = ("This link is incomplete. Open it again from your email.", "الرابط غير مكتمل. افتحه مجدداً من بريدك."),

        ["home.hello"] = ("Hello", "مرحباً"),
        ["home.openRequests"] = ("Waiting for you", "بانتظارك"),
        ["home.noRequests"] = ("Nothing is waiting for you right now.", "لا يوجد ما ينتظرك حالياً."),
        ["home.keyInfo"] = ("Key information", "معلومات رئيسية"),
        ["home.noKeyInfo"] = ("Approved documents will show their key details here.", "ستظهر هنا التفاصيل الرئيسية للمستندات المعتمدة."),
        ["home.metrics"] = ("Your figures", "أرقامك"),
        ["home.recent"] = ("Recent documents", "أحدث المستندات"),
        ["home.noDocuments"] = ("No documents have been shared with you yet.", "لم تتم مشاركة أي مستندات معك بعد."),
        ["home.viewAll"] = ("View all", "عرض الكل"),
        ["home.upload"] = ("Upload", "رفع"),
        ["home.expires"] = ("Expires", "ينتهي"),
        ["home.daysLeft"] = ("days left", "يوم متبقٍ"),
        ["home.vsLastYear"] = ("vs previous year", "مقارنة بالعام السابق"),

        ["docs.title"] = ("Your documents", "مستنداتك"),
        ["docs.search"] = ("Search by file name", "ابحث باسم الملف"),
        ["docs.allCategories"] = ("All categories", "كل الفئات"),
        ["docs.empty"] = ("No documents match.", "لا توجد مستندات مطابقة."),
        ["docs.download"] = ("Download", "تنزيل"),
        ["docs.uploaded"] = ("Added", "أضيف"),
        ["docs.size"] = ("Size", "الحجم"),
        ["docs.category"] = ("Category", "الفئة"),
        ["docs.status"] = ("Status", "الحالة"),
        ["docs.expiry"] = ("Expiry date", "تاريخ الانتهاء"),
        ["docs.back"] = ("Back to documents", "العودة للمستندات"),
        ["docs.notFound"] = ("This document is not available.", "هذا المستند غير متاح."),

        ["req.title"] = ("Document requests", "طلبات المستندات"),
        ["req.subtitle"] = ("Upload the files your account manager asked for.", "ارفع الملفات التي طلبها مدير حسابك."),
        ["req.empty"] = ("You have no document requests.", "لا توجد طلبات مستندات."),
        ["req.due"] = ("Due", "الموعد"),
        ["req.upload"] = ("Upload file", "رفع ملف"),
        ["req.uploading"] = ("Uploading…", "جارٍ الرفع…"),
        ["req.uploaded"] = ("Thank you. Your file was sent for review.", "شكراً لك. تم إرسال ملفك للمراجعة."),
        ["req.failed"] = ("The upload did not go through. Try again.", "لم يكتمل الرفع. حاول مرة أخرى."),
        ["req.tooLarge"] = ("Files can be up to 10 MB.", "الحد الأقصى لحجم الملف 10 ميغابايت."),
        ["req.overdue"] = ("Overdue", "متأخر"),

        ["profile.title"] = ("Your profile", "ملفك الشخصي"),
        ["profile.name"] = ("Name", "الاسم"),
        ["profile.company"] = ("Company", "الشركة"),
        ["profile.phone"] = ("Phone", "الهاتف"),
        ["profile.lastLogin"] = ("Last sign-in", "آخر دخول"),
        ["profile.language"] = ("Language", "اللغة"),
        ["profile.saveLanguage"] = ("Save language", "حفظ اللغة"),
        ["profile.saved"] = ("Saved.", "تم الحفظ."),
        ["profile.password"] = ("Change password", "تغيير كلمة المرور"),
        ["profile.current"] = ("Current password", "كلمة المرور الحالية"),
        ["profile.new"] = ("New password", "كلمة المرور الجديدة"),
        ["profile.changePassword"] = ("Update password", "تحديث كلمة المرور"),
        ["profile.passwordChanged"] = ("Your password was changed.", "تم تغيير كلمة المرور."),
        ["profile.passwordWrong"] = ("The current password is not correct.", "كلمة المرور الحالية غير صحيحة."),
        ["profile.support"] = ("Need help?", "تحتاج مساعدة؟"),

        ["common.loading"] = ("Loading…", "جارٍ التحميل…"),
        ["common.error"] = ("Something went wrong. Try again.", "حدث خطأ ما. حاول مرة أخرى."),
        ["common.retry"] = ("Try again", "حاول مجدداً"),
        ["common.noSlug"] = ("Open the portal from the link in your email.", "افتح البوابة من الرابط الموجود في بريدك."),

        ["status.Open"] = ("Open", "مفتوح"),
        ["status.Submitted"] = ("Sent for review", "قيد المراجعة"),
        ["status.Completed"] = ("Approved", "معتمد"),
        ["status.Cancelled"] = ("Cancelled", "ملغى"),
        ["status.Ready"] = ("Ready", "جاهز"),
        ["status.NeedsReview"] = ("In review", "قيد المراجعة"),
        ["status.Queued"] = ("Queued", "في الانتظار"),
        ["status.Processing"] = ("Processing", "قيد المعالجة"),
        ["status.Rejected"] = ("Rejected", "مرفوض"),
        ["status.valid"] = ("Valid", "ساري"),
        ["status.expiring"] = ("Expiring soon", "ينتهي قريباً"),
        ["status.expired"] = ("Expired", "منتهي"),
    };
}
