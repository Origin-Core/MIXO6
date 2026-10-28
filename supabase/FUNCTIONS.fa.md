# فعال‌سازی ورود با Username در MIXO6

ورود با ایمیل مستقیماً به Supabase Auth وصل می‌شود. برای ورود با username، نباید ایمیل‌های کاربران از یک RPC عمومی استخراج شوند؛ به همین دلیل این پروژه از Edge Function محدودشده استفاده می‌کند.

## مراحل ویندوز / ریشه‌ی مخزن GitHub

1. بعد از اینکه کلید `sb_secret_...` قبلی را که در گفتگو افشا شده لغو کردی، در Supabase Dashboard یک Secret key جدید بساز.
2. نصب Node.js LTS را انجام بده و در PowerShell داخل ریشه‌ی مخزن اجرا کن:

```powershell
npx supabase@latest login
npx supabase@latest link --project-ref tgltkobvufqazjrjckpe
```

3. Secret جدید را فقط در محیط Function ثبت کن (نمونه زیر را با مقدار جدید خودت جایگزین کن؛ خود مقدار را در گفتگو یا GitHub commit نکن):

```powershell
$env:MIXO6_SUPABASE_SECRET_KEY = Read-Host "کلید Secret تازه‌ی Supabase را وارد کن"
npx supabase@latest secrets set "MIXO6_SUPABASE_SECRET_KEY=$env:MIXO6_SUPABASE_SECRET_KEY"
Remove-Item Env:MIXO6_SUPABASE_SECRET_KEY
```

4. SQL migration را ابتدا در Dashboard → SQL Editor اجرا کن؛ این migration تابع rate-limit سروری را هم می‌سازد.
5. Function را Deploy کن:

```powershell
npx supabase@latest functions deploy mixo6-login
```

6. در Function Logs، خطای پیکربندی یا درخواست‌های محدودشده را بررسی کن. Secret کلید نباید در سورس، appsettings، فایل `.env` commitشده یا اسکرین‌شات عمومی باشد.

## امنیت

- Edge Function کلید privileged را فقط در محیط سرور می‌خواند و کلید را به برنامه نمی‌دهد.
- نام کاربری به ایمیل داخلی تبدیل می‌شود؛ ایمیل در پاسخ ورود یا خطای ورود فاش نمی‌شود.
- تلاش‌های username login به ۱۰ تلاش در هر ۱۵ دقیقه برای هر IP hash محدود می‌شود؛ IP خام در جدول ذخیره نمی‌شود.
- اگر Function deploy نشده یا SQL کامل اجرا نشده باشد، ورود با username کار نمی‌کند؛ ورود با ایمیل از Auth همچنان مسیر مستقل خود را دارد.
