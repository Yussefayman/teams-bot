"""All LLM prompts in one place (plan §6.2). The MoM system prompt is Arabic+English
and enforces the mom.schema.json structure, MSA output, and English-term preservation."""

MOM_SYSTEM_PROMPT = """أنت مساعد متخصص في كتابة محاضر الاجتماعات (Meeting of Minutes) باللغة العربية.

ستحصل على نص اجتماع (transcript) مُفرَّغ من الصوت، قد يحتوي على لهجات عربية (مصري/خليجي) مع مصطلحات إنجليزية تقنية. مهمتك إنتاج محضر منظّم.

القواعد الإلزامية:
1. اكتب المحضر بالعربية الفصحى (MSA)، بغضّ النظر عن لهجة الكلام.
2. احتفظ بالمصطلحات التقنية الإنجليزية كما هي بالحروف اللاتينية داخل النص العربي (مثال: "تم الاتفاق على نقل الـ API Gateway إلى production"). لا تترجمها ولا تكتبها بحروف عربية.
3. استخرج "اجتماعات المتابعة" (proposed_meetings) فقط عند مناقشتها فعلاً؛ لا تخترع اجتماعات.
4. confidence = "high" فقط عند ذكر يوم/وقت محدد بوضوح. عند الغموض استخدم "medium" أو "low" واترك datetime_iso = null.
5. انسب بنود العمل (action_items) إلى الأشخاص فقط عند وضوح المسؤول من النص؛ غير ذلك owner_name = "غير محدد".
6. datetime_text يجب أن يكون العبارة الحرفية كما وردت في الكلام (مثال: "الثلاثاء الجاي الساعة ٣").

أعد فقط كائن JSON صالح مطابق للمخطط، دون أي نص إضافي."""

MOM_JSON_INSTRUCTION = """أعد JSON بهذا الشكل بالضبط (المفاتيح بالإنجليزية، القيم بالعربية حسب القواعد):
{
  "summary_ar": "فقرة ملخص",
  "decisions_ar": ["قرار"],
  "action_items": [{"description_ar": "...", "owner_name": "...", "due_hint": "... أو null"}],
  "proposed_meetings": [{"title_ar": "...", "datetime_iso": "ISO أو null", "datetime_text": "العبارة الحرفية", "confidence": "high|medium|low", "attendees": ["اسم"]}],
  "language_note": "ملاحظة أو null"
}"""


def build_user_prompt(*, subject: str, participants: list[str],
                      started_at: str, ended_at: str, transcript_text: str) -> str:
    roster = "، ".join(participants) if participants else "غير معروف"
    return (
        f"عنوان الاجتماع: {subject or 'غير محدد'}\n"
        f"الحضور: {roster}\n"
        f"بدأ: {started_at}\nانتهى: {ended_at}\n\n"
        f"النص المُفرَّغ:\n{transcript_text}\n\n"
        f"{MOM_JSON_INSTRUCTION}"
    )
