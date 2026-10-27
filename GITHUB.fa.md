# جایگزینی سورس MIXO6 در مخزن GitHub موجود

این راهنما برای وقتی است که مخزن MIXO6 را از قبل در GitHub داری. فایل‌های این بسته را روی کپی محلی همان مخزن کپی کن؛ پوشه‌ی مخفی `.git` را پاک نکن.

## 1) فایل‌ها را جایگزین کن

1. ZIP نسخه‌ی نهایی سورس را در یک پوشه‌ی موقت Extract کن.
2. **محتویات داخل ZIP** (از جمله `.github`، `src` و `supabase`) را مستقیماً به ریشه‌ی مخزن محلی خودت کپی کن و فایل‌های هم‌نام را جایگزین کن.
3. پوشه‌ی `.git` مخزن فعلی را نگه دار. هیچ فایل رمز، `.env`، کلید `sb_secret`، رمز SMTP یا رمز دیتابیس را به مخزن اضافه نکن.
4. در PowerShell داخل ریشه‌ی مخزن اجرا کن:

```powershell
git status
git add -A
git commit -m "Finalize MIXO6 Supabase integration"
git push
```

اگر مخزن تو روی شاخه‌ای غیر از شاخه‌ی فعلی کار می‌کند، `git push` شاخه‌ی فعلی را به remote تنظیم‌شده می‌فرستد. برای دیدن نام شاخه:

```powershell
git branch --show-current
git remote -v
```

## 2) ساخت EXE در GitHub Actions

- با هر Push به `main` یا `master`، Workflow مربوط به Windows CI اجرا می‌شود. از تب `Actions` بررسی کن که سبز شود؛ خروجی CI یک Artifact موقت است.
- برای ساخت GitHub Release، از Tag جدید استفاده کن. اگر `v1.0.0` قبلاً استفاده شده، آن را دوباره نساز. مثلاً:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

پس از اجرای موفق workflow، از بخش `Releases` فایل `MIXO6-win-x64.zip` را بگیر. این ZIP شامل برنامه‌ی Windows x64 است.

- اگر workflow اجازه‌ی انتشار Release نداشت، در GitHub برو به `Settings → Actions → General → Workflow permissions` و گزینه‌ی `Read and write permissions` را فعال کن. Workflow انتشار خودش `contents: write` درخواست می‌کند.
- برای کلید Publishable عمومی Supabase نیازی به GitHub Secret نیست؛ داخل تنظیمات پیش‌فرض کلاینت قرار دارد.
- کلید `sb_secret`، `service_role`، رمز SMTP و رمز دیتابیس را در هیچ فایل پروژه یا GitHub Actions قرار نده.

## 3) کارهایی که هنوز در پنل Supabase انجام می‌شود

1. مهاجرت `supabase/migrations/20261027000000_mixo6.sql` را در SQL Editor اجرا کن.
2. `Confirm email` را روشن و SMTP اختصاصی را طبق `supabase/SMTP.fa.md` تنظیم کن.
3. پس از لغو Secret key افشاشده و ساخت Secret جدید، ورود با نام کاربری را طبق `supabase/FUNCTIONS.fa.md` deploy کن.
4. با دو حساب ایمیل واقعی لابی بساز و Join را تست کن.

## محدودیت تست

CI ساخت برنامه روی Windows را بررسی می‌کند؛ آن به‌تنهایی اتصال واقعی Supabase، رسیدن ایمیل به Inbox کاربران، سنسور دمای تمام سخت‌افزارها یا اعمال DNS را روی تمام مدل‌های Windows اثبات نمی‌کند. پیش از انتشار عمومی، تست واقعی لازم است.
