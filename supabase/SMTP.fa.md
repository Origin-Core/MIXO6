# تنظیم SMTP برای ایمیل ثبت‌نام واقعی MIXO6

در Supabase گزینه‌ی **Enable custom SMTP** را روشن کن. این کار اجازه می‌دهد ایمیل تأیید برای کاربران عادی هم ارسال شود؛ SMTP پیش‌فرض Supabase برای تست است و طبق مستندات فعلی فقط ایمیل اعضای تیم پروژه را می‌پذیرد و سقف پیش‌فرض آن ۲ ایمیل در ساعت است. منبع: https://supabase.com/docs/guides/auth/auth-smtp

چون فعلاً دامنه و سرویس ایمیل تراکنشی نداری، دو گزینه داری. **گزینه‌ی Gmail راه‌اندازی اولیه را بدون خرید دامنه ممکن می‌کند، اما برای محصول عمومی بلندمدت حرفه‌ای یا نامحدود نیست.**

## گزینه A — شروع بدون دامنه با Gmail (مناسب شروع کم‌حجم)

شرط: یک حساب Gmail داشته باشی که تنظیم App Password برایش در دسترس باشد. رمز اصلی Gmail را در Supabase وارد نکن.

1. در Google Account → `Security`، تأیید دومرحله‌ای (2-Step Verification) را روشن کن.
2. بخش `App passwords` را باز و یک App Password برای MIXO6 ایجاد کن. اگر گوگل این گزینه را برای حسابت نمایش نمی‌دهد، این روش در دسترس حسابت نیست؛ سراغ گزینه B یا یک ارائه‌دهنده‌ی دیگر برو.
3. در Supabase Dashboard → `Authentication` → `Emails` → `SMTP Settings` این مقادیر را وارد کن:

| فیلد Supabase | مقدار دقیق |
|---|---|
| Enable custom SMTP | **ON / روشن** |
| Sender email address | همان آدرس کامل Gmail، مثلاً `youraccount@gmail.com` |
| Sender name | `MIXO6` |
| SMTP host | `smtp.gmail.com` |
| SMTP port | `465` |
| SMTP username | همان آدرس کامل Gmail |
| SMTP password | App Password شانزده‌کاراکتری که از Google ساخته‌ای؛ نه رمز عادی Gmail |

در این مثال `youraccount@gmail.com` فقط قالب نمونه است؛ آن را با ایمیل واقعی خودت جایگزین کن. رمز برنامه را فقط داخل پنل Supabase وارد کن؛ در گیت‌هاب، سورس برنامه، SQL یا این گفتگو قرار نده. Gmail رسماً از `smtp.gmail.com` و پورت `465` برای SSL پشتیبانی می‌کند: https://developers.google.com/workspace/gmail/imap/imap-smtp

### محدودیت Gmail

برای حساب شخصی Gmail ممکن است سقف ارسال حدود ۵۰۰ پیام در روز اعمال شود و گوگل می‌تواند پیام‌های مشکوک را رد کند یا حساب را موقتاً محدود کند. این سرویس نامحدود نیست و فرستنده‌ی ایمیل با نام دامنه‌ی MIXO6 نخواهد بود. منبع: https://support.google.com/mail/answer/22839?hl=en

برای شروع، در `Authentication → Rate Limits` سقف ایمیل را محافظه‌کارانه بگذار؛ مثلاً حداکثر ۲۰ ایمیل در ساعت، سپس پس از تست تحویل و بررسی محدودیت واقعی حساب، آن را تنظیم کن. محدودیت Supabase و Gmail هر دو اعمال می‌شوند.

## گزینه B — سرویس ایمیل تراکنشی با دامنه‌ی خودت (بهتر برای محصول عمومی)

این گزینه ظاهر حرفه‌ای‌تری دارد و برای جداسازی ایمیل‌های ورود از ایمیل شخصی بهتر است؛ اما نیاز به دامنه‌ی تحت مالکیت تو دارد. دامنه‌ی `github.io` متعلق به GitHub است، نه تو، پس از آن برای فرستنده‌ی ایمیل استفاده نکن.

### نمونه‌ی تنظیم با Resend، اگر ثبت‌نام و استفاده از آن برایت مجاز و در دسترس است

پلن رایگان فعلی Resend تا ۳۰۰۰ ایمیل در ماه و ۱۰۰ ایمیل در روز اعلام می‌کند؛ سهمیه تغییرپذیر است و نامحدود نیست: https://resend.com/pricing

1. دامنه‌ی خودت را در `Domains` سرویس اضافه کن.
2. رکوردهای SPF و DKIM که همان داشبورد ارائه می‌کند، در DNS دامنه وارد کن و وضعیت را Verified کن. رکوردها را حدس نزن.
3. یک API key ارسال ایمیل ایجاد کن.
4. در Supabase Custom SMTP این مقادیر را بگذار:

| فیلد Supabase | مقدار |
|---|---|
| Enable custom SMTP | روشن |
| Sender email address | `no-reply@YOUR-VERIFIED-DOMAIN` (از دامنه‌ی تأییدشده‌ی خودت) |
| Sender name | `MIXO6` |
| SMTP host | `smtp.resend.com` |
| SMTP port | `465` |
| SMTP username | `resend` |
| SMTP password | API key واقعی Resend |

منبع رسمی اطلاعات SMTP: https://resend.com/changelog/smtp-service

## قالب ایمیل کد تأیید

در `Authentication` → `Email Templates` → `Confirm signup` قالب را طوری تنظیم کن که کد OTP را نشان دهد، چون MIXO6 کد شش‌رقمی می‌گیرد. نمونه:

```html
<h2>تأیید حساب MIXO6</h2>
<p>کد تأیید شما:</p>
<p style="font-size:28px;font-weight:bold;letter-spacing:6px">{{ .Token }}</p>
<p>اگر این ثبت‌نام را درخواست نکرده‌ای، این ایمیل را نادیده بگیر.</p>
```

`Confirm email` را روشن نگه دار. سپس با یک آدرس ایمیل واقعی ثبت‌نام کن و Inbox، Spam و Auth Logs را بررسی کن. قبل از تأیید SMTP، ارسال به تمام آدرس‌های عمومی را فعال فرض نکن.
