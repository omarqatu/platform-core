# PLATFORM_CORE — الملحق 01: جاهزية المنتج

**الحالة:** معتمد من صاحب المشروع — 2026-10-17. يلحق بـ PLATFORM_CORE v1.16 (المجمّدة) ولا يعدّلها. `_EN.md` ترجمة؛ العربي حاكم.

## §0 الحالة والسلطة

**النفاذ:** من تاريخ دمج هذا الملف. البنود المقابلة في `OPEN_ITEMS.md`: 42–49.

**ما يعدّله:** PLATFORM_CORE v1.16 المجمّدة — **بالإضافة لا بالتعديل**. لا سطر من 1.16 يُحذف أو يُعاد صياغته؛ هذا الملحق يضيف جداول وأعمدة ومنحاً وسياسات ومسارات، ويسجّل قرارات. كل بند هنا يذكر الفقرة التي يلحق بها.

**سلسلة السلطة بعده:** PLATFORM_CORE v1.16 + الملحقات بترتيبها (01، 02…) → PROOF_SPEC v1.3 → الكود → الأدلة. تعارض بين 1.16 وملحق = خطأ في الملحق حتى يقرر صاحب المشروع غير ذلك. يُضاف هذا السطر إلى `CLAUDE.md`.

**ما لا يمسّه (ثابت بنص 1.16):**

- الجهة وحدة العزل الوحيدة (§4.8)؛ المحور الثاني تضييق لا وحدة.
- الأدوار الخمسة، لا عضوية بين أدوار، `BYPASSRLS` للـ migrator وحده، لا `SECURITY DEFINER` (§3.4).
- القالبان القياسيان للسياسات، `NULLIF`، `AS RESTRICTIVE` للتضييق، FK مركّبة بين جدولين بـ `tenant_id` (§3.3).
- التطبيق يولّد كل مفتاح وطابع زمني؛ لا `DEFAULT`/identity/generated (§3.5).
- مبدأ الموافقة: لا عضوية بلا قبول (§0).
- الفحوصات العشرة ومدخلاتها؛ ما يضيفه الملحق يدخل `grants.json` ولا يغيّر الـ manifest إلا حيث يذكر الملحق سياسة خارج القالبين.

**نطاق الملحق:** ما يلزم لمرحلة «استقبال نظام الاشتراكات» حسب خارطة الطريق المعتمدة — لا أكثر. محرك الفوترة، البوابات، التسجيل الذاتي العام، العلامات/الشركاء، §9، SSO، بوابة العميل: خارجه (§7).

## §1 القرارات المسجّلة

قرارات صاحب المشروع بتاريخ 2026-10-17. كل واحد يصبح بنداً في `OPEN_ITEMS.md` بالرقم الذي يعطيه.

| # | القرار | الأثر على 1.16 |
| --- | --- | --- |
| D1 | **الخلافة:** المنصة تخلف نظام الاشتراكات الحي (SYSTEM_DESIGN 2.0). تُرحَّل البيانات فقط؛ لا كود يُنقل. القديم للقراءة فقط حتى التبديل، ثم يُطفأ | يحسم الشرط المفتوح في §0 |
| D2 | **إنشاء الجهات في الإنتاج** بإجراء تشغيلي بدور `provisioner` (bootstrap من القوالب + دعوة `owner` يقبلها المالك). لا تسجيل ذاتي عام | يضيف مساراً إلى §4.4 بنفس الدور والمنح |
| D3 | **قنوات الإرسال:** بريد، واتساب (بوابة مستضافة مثل GreenAPI)، SMS (API)، تلغرام (Bot). بريد + واتساب أولاً. **الخيار أ:** يُبنى داخل M1 قبل التبديل | جداول جديدة (§2)، منح (§3) |
| D3′ | **ثلاث طبقات للإرسال:** حساب المزوّد للمنصة افتراضياً مع إمكان تجاوز لكل جهة لاحقاً؛ تفعيل القنوات ومواعيدها لكل جهة؛ جهة الاتصال لكل عميل. **أسرار المزوّدين ليست في قاعدة البيانات** (§2.6) | — |
| D4 | **تدقيق الصفوف المرحَّلة:** [قيد واحد لكل جهة «migrated from legacy» / قيد لكل صف] — **مفتوح، يحسمه صاحب المشروع** | §7 |
| D5 | **OPEN_ITEMS 27 يُؤجَّل:** إعادة القياس بعد 10 جهات حقيقية؛ العتبة والبديلان ثابتان | — |
| D6 | **استعادة كلمة السر وتغييرها** بثلاثة مسارات ووحدتَي عمل (authenticator ثم app_user)، مع `security_stamp` يُنهي كل الجلسات عند التغيير (يغلق OPEN_ITEMS 36 عند البناء) | §2.1، §2.2، §3، §5 |
| D7 | **سجل اشتراك المنصة (الطبقة 1):** خطط بلا أسعار، اشتراك لكل جهة بـ `paid_until`، إيقاف آلي، قراءة للمالك. المحرك والبوابات مؤجلان؛ يُبنيان فوق هذا لا بدله | §2.3، §2.4، §3، §4، §5 |
| D8 | **OPEN_ITEMS 25 مؤجّل** حتى الوحدة الثانية | — |
| D9 | **الملحق مستقل** عن 1.16؛ الدمج في 2.0 حين تتراكم الملحقات أو تبدأ الوحدة الثانية | سلسلة السلطة (§0) |

## §2 الجداول والأعمدة الجديدة (إضافات إلى §4 و§5)

كل جدول يحمل `tenant_id` يتبع القالب القياسي (`tenant_isolation`)، ومن يحمل `scope_ref_id` يتبع القالبين. المفاتيح `app_id` والطوابع `app_ts` كما في 1.16. أسماء الأعمدة مبدئية حتى الترحيل.

### 2.1 `users.security_stamp` — عمود جديد (إلى §4.2)

`security_stamp app_id NOT NULL`. يولّده التطبيق عند إنشاء المستخدم ويغيّره عند كل تغيير لكلمة السر. يدخل الكوكي؛ `UnitOfWorkMiddleware` يقارنه مع الصف في كل طلب (قراءة واحدة تحت `user_self_read` الموجودة)؛ اختلافه = جلسة منتهية (401). **الترحيل:** عمود `NOT NULL` على جدول فيه صفوف → backfill بقيمة لكل صف داخل نفس الترحيل، ثم فحص عدد الصفوف (قاعدة 1.16 §3.5).

### 2.2 `password_reset_tokens` — جدول عام (إلى §4.2، بجوار `auth_attempts`)

```sql
password_reset_tokens(id, user_id → users, token_hash, requested_at, expires_at, used_at NULL, request_ip NULL)
-- بلا tenant_id. RLS مفعّل. يخزَّن hash الرمز فقط (SHA-256 لرمز 32 بايت).
-- طلب جديد يضع used_at على الرموز السابقة غير المستخدمة للمستخدم نفسه.
-- الاحتفاظ: نفس سياسة auth_attempts (بند §13 في 1.16) — تنظيف تشغيلي بدور migrator.
```

### 2.3 `plans` — كتالوج عام (إلى §5)

`plans(id, code, name_ar, name_en, is_active)`. يزرعه `migrator` فقط. **لا أسعار** — السعر اتفاق خارج المنصة في هذه المرحلة. الخطط الأولى: [`trial`, `standard`] — افتراض.

### 2.4 `platform_subscriptions` — لكل جهة (إلى §4.1)

```sql
platform_subscriptions(id, tenant_id, plan_code → plans(code), status, period_start, paid_until, grace_days, created_at, updated_at,
  UNIQUE (tenant_id))                       -- اشتراك واحد نشط لكل جهة في هذه المرحلة
-- status ∈ {active, past_due, suspended}. القالب القياسي.
-- يُنشأ في bootstrap (provisioner) بخطة trial وpaid_until = [اليوم + 30] — افتراض.
-- تتغير paid_until/status بالإجراء التشغيلي (provisioner) أو بالمهمة الخلفية (job_runner).
```

**الحدّ الصريح:** هذا ليس فاتورة ولا دفعة. لا `invoices` ولا `payments` في هذا الملحق.

### 2.5 `tenant_notification_settings` — لكل جهة (إلى §4.1؛ ملكيته الوحدة)

`(id, tenant_id, channel, is_enabled, reminder_days_before int[], sender_display_name, created_at, updated_at, UNIQUE (tenant_id, channel))`. `channel ∈ {email, whatsapp, sms, telegram}`. القالب القياسي. تُنشأ الصفوف عند تفعيل الوحدة للجهة بقيم من النظام الحي [—].

### 2.6 `provider_accounts` — المنصة مع تجاوز اختياري للجهة (إلى §4.1)

```sql
provider_accounts(id, tenant_id NULL, channel, provider_code, sender_id, credential_ref, is_active, created_at,
  UNIQUE (tenant_id, channel))              -- وصفّ المنصة: tenant_id IS NULL
-- credential_ref = اسم المفتاح في إعدادات Worker/Api (env/secret store). **لا سرّ في قاعدة البيانات.**
-- في هذه المرحلة تُزرع صفوف المنصة فقط (migrator). صفوف الجهات: باب محجوز، لا مسار يكتبها.
```

لماذا: `app_user` يقرأ هذا الجدول في كل معاملة إرسال؛ لو حمل مفاتيح المزوّدين لكان كل عضو جهة على بعد ثغرة واحدة من مفاتيح المنصة. المرجع إلى الإعدادات يُبقي السرّ حيث تعيش سلاسل الاتصال. **سياسة القراءة** تحتاج صياغة خارج القالب: `tenant_id = app.tenant_id OR tenant_id IS NULL` — سياسة مُسمّاة تدخل الـ manifest (Check 2).

### 2.7 `notification_log` — لكل جهة ومُنطَّق (إلى الوحدة، بجوار `subscriptions`)

```sql
notification_log(id, tenant_id, subscription_id, scope_ref_id, channel, provider_message_id NULL, status, attempted_at, sent_at NULL, error NULL,
  FOREIGN KEY (tenant_id, subscription_id) REFERENCES subscriptions (tenant_id, id))
-- scope_ref_id = عميل الاشتراك → القالبان. status ∈ {pending, sent, failed}.
-- يُكتب pending في معاملة الحساب (outbox)؛ خطوة الإرسال تنقله إلى sent|failed.
-- لا DELETE لأي دور تطبيقي؛ الحالة تتقدم فقط.
-- مفتاح عدم التكرار: UNIQUE (tenant_id, subscription_id, channel, reminder_day) حيث reminder_day = تاريخ الإرسال المخطط.
```

### 2.8 أعمدة على `clients` (إلى الوحدة)

`email NULL`, `phone_e164 NULL`, `telegram_chat_id NULL`, `is_active NOT NULL` (تعطيل لا حذف). كلها بنفس منح `clients` الحالية.

### 2.9 أعمدة على `subscriptions` (إلى الوحدة)

`status ∈ {active, cancelled, renewed}`, `renewed_from_id NULL → subscriptions(tenant_id, id)` (مركّب). التجديد = اشتراك جديد يشير إلى القديم؛ الإلغاء = تغيير حالة. **لا DELETE** — يبقى كما في 1.16 للوحدات لكن الوحدة لا تستعمله.

### 2.10 `legacy_id` — مؤقت

عمود `legacy_id text NULL` على `clients` و`subscriptions` و`tenants` و`users` يضيفه ترحيل الاستيراد ويحذفه ترحيل لاحق بعد التبديل. وجوده بعد الإطفاء خطأ.

## §3 مصفوفة المنح كاملة محدّثة (تحل محل §3.8 عند الدمج)

الأسطر من 1.16 منقولة حرفياً. الجديد **بالخط العريض**. ما ليس في المصفوفة ليس ممنوحاً. تُنقل إلى `checks/core/grants.json` في PR الكود، ويفشل Check 6 حتى تتطابق.

| الجدول | app_user | authenticator | provisioner | job_runner |
| --- | --- | --- | --- | --- |
| tenants | SELECT | — | INSERT | SELECT, **UPDATE (status)** — الإيقاف الآلي فقط (§5.3) |
| persons | SELECT | — | SELECT, INSERT | — |
| users | SELECT, UPDATE (last_login_at, language, theme, **security_stamp**) | SELECT | SELECT, INSERT | — |
| user_password_credentials | UPDATE (password_hash, updated_at) بلا SELECT | SELECT | INSERT | — |
| memberships | SELECT, UPDATE (status) | — | SELECT, INSERT, UPDATE (status) | — |
| membership_roles | SELECT, INSERT, DELETE | — | INSERT, SELECT, DELETE | — |
| membership_auth | SELECT | SELECT | INSERT | — |
| membership_scope | SELECT, UPDATE (scope_mode) | — | INSERT, SELECT, UPDATE (scope_mode) | — |
| scope_assignments | SELECT, INSERT, UPDATE (active) | — | SELECT, UPDATE (active) | — |
| invitations | SELECT, INSERT, UPDATE (status) | — | SELECT, UPDATE (status) | — |
| auth_attempts | — | SELECT, INSERT | — | — |
| **password_reset_tokens** | — | **SELECT, INSERT, UPDATE (used_at)** | — | — |
| audit_log | SELECT (مشروط بـ scope_all), INSERT | — | INSERT | — |
| roles | SELECT, INSERT, UPDATE, DELETE | — | SELECT, INSERT | — |
| role_permissions | SELECT, INSERT, DELETE | — | INSERT | — |
| tenant_modules | SELECT, UPDATE (is_active) | — | INSERT | — |
| modules | SELECT | — | SELECT | — |
| permissions | SELECT | — | SELECT | — |
| role_templates | — | — | SELECT | — |
| role_template_permissions | — | — | SELECT | — |
| **plans** | **SELECT** | — | **SELECT** | **SELECT** |
| **platform_subscriptions** | **SELECT** (بصلاحية `core.billing.read` في المسار) | — | **INSERT, SELECT, UPDATE (status, paid_until, period_start, plan_code)** | **SELECT, UPDATE (status)** |
| **provider_accounts** | **SELECT** | — | — | — |
| **tenant_notification_settings** | **SELECT, INSERT, UPDATE (is_enabled, reminder_days_before, sender_display_name)** | — | **INSERT** (تهيئة الوحدة عند bootstrap) | — |
| **notification_log** | **SELECT, INSERT, UPDATE (status, provider_message_id, error, sent_at)** — لا DELETE؛ الحالة تتقدم فقط (outbox) | — | — | — |
| module tables (`clients`, `subscriptions`) | SELECT, INSERT, UPDATE, DELETE (ضمن RLS) | — | — | — |

### تبرير الأسطر الجديدة

- **`job_runner` على `tenants`/`platform_subscriptions` (UPDATE status):** الإيقاف الآلي يكتب بسياق نظام قبل الدخول إلى أي جهة — لا توجد جهة «تطلب إيقاف نفسها». هذا أول حق كتابة لـ `job_runner`؛ سياسته مقيّدة بالانتقال `active|past_due → suspended` فقط (§4). الإرجاع إلى `active` بيد `provisioner` (الإجراء التشغيلي) لا الآلة.
- **`authenticator` على `password_reset_tokens`:** الاستعادة تبدأ بلا جلسة، وهو الدور الوحيد الذي يعمل بلا جلسة. الكتابة محصورة في الرمز نفسه؛ لا يلمس كلمة السر — التحديث يتم كـ `app_user` بسياق المستخدم صاحب الرمز عبر `password_self_update` الموجودة.
- **`app_user` على `provider_accounts` (SELECT فقط):** المهمة الخلفية تعمل لكل جهة كـ `app_user` (1.16 §8)، وتحتاج معرفة أي مزوّد. لا أسرار في الجدول (§2.6).
- **`notification_log` (outbox):** المهمة تكتب السطر `pending` وتغلق المعاملة؛ خطوة الإرسال تحدّثه إلى `sent|failed` في معاملة قصيرة. لا DELETE؛ سياسة مقيِّدة تسمح بالانتقال `pending → sent|failed` فقط. الإعادة = سطر جديد.
- **`provisioner` على `tenant_notification_settings` (INSERT):** يهيئ الإعدادات الافتراضية مع bootstrap كما يهيئ الأدوار من القوالب. لا SELECT: لا يحتاج قراءتها.

**ما لم يُمنح عمداً:** `app_user` لا يكتب `platform_subscriptions` (لا جهة تمدّد اشتراكها بنفسها)؛ `job_runner` لا يقرأ `users` ولا `persons`؛ لا دور جديد.

## §4 السياسات الجديدة والصلاحيات الجديدة

### 4.1 سياسات خارج القالبين (تدخل الـ manifest لـ Check 2)

كل سياسة بـ `FOR` و`TO` صريحين و`NULLIF` على `current_setting`. الأسماء مبدئية.

| السياسة | الجدول | FOR / TO | الشرط |
| --- | --- | --- | --- |
| `password_reset_tokens_authenticator_select` | password_reset_tokens | SELECT / authenticator | `true` — الدور يرى الرموز كلها؛ المطابقة بالـ hash في الاستعلام |
| `password_reset_tokens_authenticator_insert` | password_reset_tokens | INSERT / authenticator | `true` |
| `password_reset_tokens_authenticator_use` | password_reset_tokens | UPDATE / authenticator | `used_at IS NULL` → WITH CHECK `used_at IS NOT NULL` — الكتابة الوحيدة هي الاستهلاك |
| `plans_read` | plans | SELECT / app_user, provisioner, job_runner | `true` (كتالوج) |
| `platform_subscriptions_read` | platform_subscriptions | SELECT / app_user | القالب القياسي (`tenant_id = NULLIF(app.tenant_id)`) |
| `platform_subscriptions_provisioner_*` | platform_subscriptions | INSERT, SELECT, UPDATE / provisioner | `tenant_id = NULLIF(app.tenant_id)` كما في bootstrap |
| `platform_subscriptions_job_suspend` | platform_subscriptions | UPDATE / job_runner | `status IN ('active','past_due')` → WITH CHECK `status = 'suspended'` — انتقال واحد |
| `tenants_job_suspend` | tenants | UPDATE / job_runner | `status = 'active'` → WITH CHECK `status = 'suspended'` |
| `provider_accounts_read` | provider_accounts | SELECT / app_user | `tenant_id IS NULL OR tenant_id = NULLIF(app.tenant_id)` |
| `tenant_notification_settings_*` | tenant_notification_settings | القالب القياسي / app_user + INSERT / provisioner | — |
| `notification_log_*` | notification_log | SELECT, INSERT, UPDATE / app_user — وللـ UPDATE سياسة `AS RESTRICTIVE`: `status = 'pending'` → WITH CHECK `status IN ('sent','failed')` | القالبان (`tenant_isolation` + `client_scope`) |

**قاعدة 1.16 التي تنطبق هنا:** سياسات `job_runner` الجديدة تضييق انتقالات، فتُكتب بـ `AS RESTRICTIVE` فوق سياسة سماح بسيطة — لا سياسة `FOR ALL`.

### 4.2 صلاحيات جديدة (إلى §5)

| الكود | الوحدة | المعنى | القوالب |
| --- | --- | --- | --- |
| `core.billing.read` | core | قراءة اشتراك المنصة للجهة الحالية | owner, admin |
| `subscriptions.manage_settings` | subscriptions | تعديل قنوات التنبيه ومواعيدها | owner, admin |
| `subscriptions.clients.write` | subscriptions | إنشاء/تعديل عميل (التعطيل: `subscriptions.clients.manage` — owner, admin) | owner, admin, operator |
| `subscriptions.notifications.read` | subscriptions | قراءة سجل الإرسال | owner, admin, operator, viewer |

الإدخال بنفس وصفة الدليل §11: الصلاحية، القوالب، ترقية أدوار الجهات القائمة، فحص عدد الصفوف — في ترحيل واحد.

### 4.3 ما يبقى بلا صلاحية جديدة

`subscriptions.write` تغطي إنشاء الاشتراك وتجديده وإلغاءه. استعادة كلمة السر وتغييرها بلا صلاحية — مسار هوية لا مسار جهة.

## §5 المسارات: endpoints، المهام الخلفية، الإجراء التشغيلي

### 5.1 endpoints جديدة (إلى مرجع الدليل §14)

| المسار | النوع | الصلاحية | ملاحظة |
| --- | --- | --- | --- |
| `POST /api/auth/password/forgot` | own UoW (authenticator) | — | 204 دائماً؛ حدّ معدل؛ بريد عبر المُرسِل |
| `POST /api/auth/password/reset` | own UoW ×2 | — | UoW-1 authenticator يستهلك الرمز؛ UoW-2 app_user (`app.user_id` = صاحب الرمز) يحدّث الـ hash ويغيّر `security_stamp`؛ لا كوكي يُصدر |
| `PUT /api/me/password` | own UoW ×2 | session | التحقق من الحالية (authenticator) ثم التحديث (app_user)؛ الكوكي يُعاد إصداره بالختم الجديد |
| `GET /api/tenant/subscription` | normal | `core.billing.read` | الخطة، الحالة، `paid_until` |
| `GET/PUT /api/subscriptions/settings` | module | `subscriptions.read` / `subscriptions.manage_settings` | قنوات الجهة ومواعيدها |
| `POST /api/subscriptions/clients`, `PUT …/{id}`, `PUT …/{id}/status` | module | `subscriptions.clients.write` | عميل: إنشاء، تعديل، تعطيل |
| `POST /api/subscriptions/{id}/renew`, `POST …/{id}/cancel` | module | `subscriptions.write` | التجديد اشتراك جديد بـ `renewed_from_id` |
| `GET /api/subscriptions/notifications` | module | `subscriptions.notifications.read` | ScopedList |

لا مسار يقرأ `provider_accounts` أو يكتب `platform_subscriptions` من الـ API.

### 5.2 الدخول — تغييران

- **`security_stamp`** يدخل الكوكي؛ `UnitOfWorkMiddleware` يقارنه في كل طلب مع `users` (تحت `user_self_read`). اختلاف → 401 `session_expired` (كود جديد، وصفة §14).
- **حدّ المحاولات** (OPEN_ITEMS 16): `Authenticator` يعدّ `auth_attempts` الفاشلة لنفس `username` من نفس `ip` خلال [15 دقيقة]؛ بعد [10] يرفض بـ 429 `too_many_attempts` **قبل** قراءة كلمة السر. الأرقام افتراض. نفس الآلية لـ `forgot` بالبريد.

### 5.3 المهام الخلفية (إلى §8)

| المهمة | الدور ثم السياق | ماذا تفعل |
| --- | --- | --- |
| `ExpiringSubscriptionsJob` (موجودة، تُوسَّع) | job_runner يسرد الجهات → app_user لكل جهة بسياق نظام | تحسب المستحق لكل `reminder_days_before` وتكتب `notification_log` بحالة `pending` وتُغلق المعاملة؛ خطوة ثانية ترسل وتنقل كل سطر إلى `sent`/`failed` في معاملة قصيرة (outbox — §5.5)؛ فشل قناة = سطر `failed` ولا يوقف غيرها؛ فشل جهة لا يوقف البقية |
| `SuspendUnpaidTenantsJob` (جديدة) | job_runner، **بلا دخول إلى جهة** | `paid_until + grace_days < today` → `platform_subscriptions.status = suspended` و`tenants.status = suspended`، في معاملة واحدة لكل جهة، تحت سياستَي §4.1 |

الإيقاف أثره فوري بطبيعة 1.16: `POST /tenants/{id}/select` يرفض الجهة الموقوفة في الطلب التالي.

### 5.4 الإجراء التشغيلي (D2)

`src/Ops` — يعمل بدور `provisioner`، بلا مسار HTTP، أوامره:

- `create-tenant --name --owner-email [--plan trial]`: bootstrap من القوالب + `platform_subscriptions` + `tenant_notification_settings` الافتراضية + دعوة `owner` بالبريد. المالك يقبل بنفسه.
- `mark-paid --tenant --until [--plan]`: يحدّث `paid_until`/`status` إلى `active`. الطريقة الوحيدة لإرجاع جهة موقوفة.
- كل أمر: `audit_log` كأي عملية `provisioner`؛ يرفض التشغيل إن وجد سلسلة `migrator` في بيئته (نفس حارس Api).

### 5.5 المُرسِل

`INotificationSender` في `Core` (ليس في الوحدة — الدعوات وكلمة السر تستعملانه). تنفيذ لكل قناة؛ الاختيار من `provider_accounts` ثم `credential_ref` من الإعدادات. **نمط outbox (قرار 2026-10-17):** لا إرسال داخل معاملة الحساب. (1) المهمة تحسب وتكتب `notification_log` بحالة `pending` وتُغلق المعاملة — الـ UNIQUE يمنع التكرار هنا. (2) خطوة الإرسال في نفس التشغيل تقرأ `pending`، تستدعي المزوّد بمهلة 5 ث، وتحدّث السطر إلى `sent` (مع `provider_message_id`) أو `failed` (مع `error`) في معاملة قصيرة لكل سطر. سطر `pending` بقي بعد التشغيل = إعادة محاولة في التشغيل التالي، لا سطر جديد. الدعوات وكلمة السر تستعمل المُرسِل مباشرةً (طلب واحد، لا دفعة).

## §6 الترحيل من النظام الحي — قواعد ملزمة

تطبيق لشروط 1.16 §3.4 (استثناء `BYPASSRLS`) على حالة الاستيراد الوحيدة المسموح بها.

1. **بيانات لا كود** (D1). المدخل تصدير CSV/JSON من النظام الحي؛ الأداة ترحيل مُرقَّم في `src/Migrations` يعمل بدور `migrator` ولا يُشغَّل إلا مرة.
2. **كل مفتاح وطابع من التطبيق:** `uuidv7()` و`now()` صريحتان في VALUES؛ لا `DEFAULT` ولا `RETURNING`؛ `legacy_id` يُحفظ للمطابقة.
3. **خريطة ثابتة:** حساب حي → `tenants` + `platform_subscriptions`(active، `paid_until` = [—]) + `tenant_notification_settings`؛ مستخدم حي → `persons` + `users` (بلا كلمة سر؛ `security_stamp` جديد) + `memberships`(active) + `membership_roles`(owner للأول، [—] للبقية) + `membership_scope`(`all` ما لم يُحدد غيره — غياب الصف خطأ صاخب في 1.16)؛ عميل حي → `clients`؛ اشتراك حي → `subscriptions`(active).
4. **ينتهي بفحوص عدد الصفوف** لكل جدول مقابل التصدير، وبفحص: لكل `subscriptions` صف `clients` بنفس `tenant_id` (الـ FK المركّب يضمنه، والفحص يسمّيه).
5. **التدقيق** حسب D4 (مفتوح).
6. **لا إرسال أثناء الترحيل:** `notification_log` يُزرع بصفوف `sent` لما أرسله النظام القديم خلال آخر [30] يوماً — وإلا أرسلت المنصة تذكيراً ثانياً أول ليلة. هذا الصف هو الحامي الوحيد من التكرار.
7. **الدخول بعد الترحيل:** دعوة بالبريد لكل مستخدم مُرحَّل (المسار القائم للقبول «مسجّل/جديد»)؛ لا hashes تُنقل.
8. **التجربة الجافة إلزامية** على نسخة من بيانات الإنتاج قبل التبديل، وتقرير «المستحق خلال 30 يوماً» يطابق النظام القديم صفاً بصف.
9. **`legacy_id` يُحذف** بترحيل لاحق بعد إطفاء القديم؛ بقاؤه خطأ.

## §7 خارج الملحق صراحةً، والأسئلة المفتوحة

### خارج الملحق (لا يُبنى تحته)

| البند | يحتاج |
| --- | --- |
| فواتير، دفعات، PDF، proration، dunning | ملحق 02 بسطح كتابة webhook جديد |
| بوابات الدفع (محلية، Stripe) — تنفيذ `IPaymentGateway` | ملحق 02 + حسم الكيان المتعاقد والعملة |
| التسجيل الذاتي العام | مع الفوترة |
| صفوف `provider_accounts` لجهة (رقم المكتب) | قرار منتج + مسار كتابة |
| تفعيل الوحدات (25)، موطن الكيان المُنطَّق (§9) | مع الوحدة الثانية |
| العلامات/الدومينات/الشريك | ملحق مستقل |
| SSO، step-up (35)، بوابة العميل | طلب فعلي |
| حذف `GET /subscriptions/screen` الخادمية (29) | قرار مستقل |

### الأسئلة الستة — أُجيبت واعتُمدت 2026-10-17

1. **D4** — تدقيق الصفوف المرحَّلة: قيد لكل جهة أم لكل صف؟
2. **أعمار وحدود** (كلها افتراضات موسومة): رمز الاستعادة 30 د؛ حدّ الدخول 10 محاولات/15 د؛ مهلة المزوّد 5 ث؛ `grace_days` الافتراضي [7]؛ `trial` [30] يوماً.
3. **القوالب**: `core.billing.read` للـ admin أيضاً أم للـ owner وحده؟
4. **`subscriptions.clients.write` للـ operator**: يُنشئ عملاء أم يكتب اشتراكات فقط؟
5. **الإرسال داخل المعاملة** (§5.5): مقبول أن يطول COMMIT بمقدار مهلة المزوّد؟ البديل (outbox + مهمة إرسال) أصحّ هندسياً وأثقل بناءً — أقترحه للملحق 02 إن ظهرت مشكلة.
6. **`provider_accounts` للمنصة**: يزرعه migrator (ثابت في الترحيل) أم الإجراء التشغيلي؟

**الإجابات المعتمدة:**

1. **D4:** قيد `audit_log` واحد لكل جهة («migrated from legacy system»، عدد الصفوف لكل جدول)؛ `legacy_id` للتتبع.
2. **الحدود:** 30 د للرمز؛ 10 محاولات/15 د؛ 5 ث للمزوّد؛ `grace_days` 7؛ `trial` 30 يوماً — كلها إعدادات لا ثوابت.
3. **`core.billing.read`:** owner وadmin.
4. **الـ operator** يُنشئ ويعدّل العملاء (`clients.write`)؛ التعطيل `clients.manage` لـ owner/admin.
5. **outbox** بدل الإرسال داخل المعاملة (§5.5، §3، §4.1 محدّثة).
6. **`provider_accounts` للمنصة** يزرعه migrator مع الكتالوجات.

### ما يلي اعتماد الملحق

- PR وثائق: `docs/PLATFORM_CORE_AMENDMENT_01.md` + `_EN.md`، سطر السلطة في `CLAUDE.md`، بنود `OPEN_ITEMS.md` بأرقامك.
- ثم PRs الكود بترتيب الخارطة، كلٌّ على فرعه، وكل ترحيل يعيد توليد `docs/schema.sql` ويحدّث `DATABASE.md`.
