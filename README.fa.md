# MIXO6 — Gaming Control Center

MIXO6 برنامه‌ی دسکتاپ Windows 10/11 با هویت بصری اختصاصی Origin Core است. بخش‌های محلی برنامه روی خود ویندوز کار می‌کنند؛ حساب کاربری و تورنومنت از Supabase Auth + PostgreSQL استفاده می‌کنند و نیازی به میزبانی Node.js جداگانه ندارند.

## امکانات پروژه

- داشبورد سخت‌افزار و ویجت شناور CPU/GPU (فقط سنسور واقعی؛ در نبود سنسور `--°C` نمایش داده می‌شود).
- انتخاب کارت شبکه، اعمال واقعی DNS روی Windows، بازگردانی DNS خودکار، تست Ping و پاک‌کردن DNS Cache.
- پروفایل‌های DNS پیشنهادی برای CS2، Minecraft و آدرس‌های سفارشی Origin.
- میانبرهای FARSI ORIGIN، RTX Game و Vidox Toolbox.
- ثبت‌نام، تأیید ایمیل، ورود و نشست محلی رمزگذاری‌شده با DPAPI ویندوز.
- ساخت/نمایش/پیوستن به لابی‌های 1v1. آدرس سرور بازی در لیست عمومی نمایش داده نمی‌شود و فقط بعد از عضویت از RPC امن دیتابیس برگردانده می‌شود.
- تنظیم Supabase Project URL و Publishable/anon key از رابط برنامه.
- GitHub Actions برای ساخت نسخه‌ی self-contained ویندوز x64 و انتشار GitHub Release.

## راه‌اندازی Supabase

**راهنمای مرحله‌به‌مرحله فارسی:** [`supabase/SETUP.fa.md`](supabase/SETUP.fa.md)

خلاصه:

1. یک Supabase Project بساز. برای تست اولیه می‌توان Central EU (Frankfurt) را امتحان کرد، اما اتصال بدون VPN از تمام ISPهای ایران تضمین‌شده نیست.
2. فایل `supabase/migrations/20261027000000_mixo6.sql` را در `SQL Editor` پنل Supabase اجرا کن.
3. Email provider و `Confirm email` را روشن کن و برای ثبت‌نام عمومی SMTP اختصاصی تنظیم کن. SMTP پیش‌فرض Supabase برای ایمیل کاربران عادی/محصول عمومی مناسب نیست.
4. در MIXO6 → تنظیمات، Project URL و Publishable/anon key را وارد کن و تست اتصال را بزن.
5. ثبت‌نام و لابی را با دو حساب واقعی تست کن.

مهم: فقط Publishable/anon key مجاز است داخل کلاینت باشد؛ هرگز `service_role`، `sb_secret`، رمز دیتابیس یا SMTP را در EXE/GitHub قرار نده.

## ساخت محلی در Windows 10/11

1. .NET 10 SDK را نصب کن.
2. PowerShell را در ریشه‌ی مخزن باز کن.
3. اجرا کن:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1
```

اسکریپت نسخه‌ی `win-x64` را منتشر می‌کند و `MIXO6.exe` و `MIXO6-win-x64.zip` را در `artifacts` می‌سازد. نسخه self-contained است و برای اجرا روی رایانه کاربر نیاز به نصب جداگانه‌ی .NET ندارد.

## GitHub Release

مخزن را Push کن، سپس Tag بساز و Push کن:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Workflow داخل `.github/workflows/windows-release.yml` روی Windows اجرا می‌شود، برنامه را publish و به GitHub Release متصل می‌کند. ممکن است لازم باشد در Settings → Actions → General، دسترسی `Read and write permissions` برای `GITHUB_TOKEN` فعال باشد.

## محدودیت‌ها و صداقت تست

- فایل EXE نهایی باید در GitHub Actions یا Windows build شود؛ ابزار .NET ویندوز در محیط تولید فایل وجود ندارد.
- تست نهایی Supabase، DNS، سنسورها روی مدل‌های مختلف سخت‌افزار و اتصال واقعی از ISPهای ایران هنوز باید انجام شود؛ ادعای «بدون هیچ باگ» پیش از تست واقعی درست نیست.
- Supabase Free: در حال حاضر 500MB دیتابیس، 50,000 MAU، 5GB ترافیک خروجی و 1GB فایل؛ پروژه‌ی کم‌فعالیت ممکن است پس از ۷ روز pause شود. بک‌آپ قابل دانلود دیتابیس در پلن رایگان ارائه نمی‌شود. محدودیت‌ها را دوباره در صفحه‌ی رسمی قیمت‌گذاری بررسی کن.
- برای ایمیل تأیید کاربران عمومی باید SMTP اختصاصی تنظیم شود؛ سرویس ایمیل پیش‌فرض Supabase فقط برای تست و ایمیل‌های از پیش مجاز مناسب است.
- برنامه باید از نظر اتصال بدون VPN در اینترنت واقعی ایران آزمایش شود. نزدیک‌بودن منطقه‌ی Frankfurt به‌تنهایی دسترسی را تضمین نمی‌کند.

## وب‌سایت‌ها

- https://origin-core.github.io/Farsi-Origin/
- https://rtx-game.rtx-game.workers.dev/
- https://vidox-ir.github.io/toolbox/
