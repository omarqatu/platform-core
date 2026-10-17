# PLATFORM_CORE — الملحق 01: جاهزية المنتج

**الحالة:** معتمد من صاحب المشروع — 2026-10-17. يلحق بـ PLATFORM_CORE v1.16 (المجمّدة): يضيف إليها، ويعدّل منها الفقرات المسمّاة في §0 وحدها. `_EN.md` ترجمة؛ العربي حاكم.

## §0 الحالة والسلطة

**النفاذ:** من تاريخ دمج هذا الملف. البنود المقابلة في `OPEN_ITEMS.md`: 42–52.

**ما يعدّله:** PLATFORM_CORE v1.16 المجمّدة. الأصل **الإضافة**: جداول وأعمدة ومنح وسياسات ومسارات، وقرارات مسجّلة؛ كل بند هنا يذكر الفقرة التي يلحق بها. **ويعدّل** الفقرات التالية من 1.16 — لا غيرها — بقرار صاحب المشروع بتاريخ 2026-10-17:

| فقرة 1.16 | نصّها في 1.16 | التعديل |
| --- | --- | --- |
| §4.3-أ/2 | «grants: `SELECT` على `users`، `user_password_credentials`، `membership_auth`؛ و`INSERT` + `SELECT` على `auth_attempts` فقط — الكتابة الوحيدة» لـ `authenticator` | يُضاف `INSERT` و`UPDATE (used_at)` على `password_reset_tokens` (§2.2، §3): الاستعادة تبدأ بلا جلسة، ولا دور غيره يعمل بلا جلسة. **ويُضاف `SELECT (id, email)` على `persons` — العمودان وحدهما** (§3، §5.1): `forgot` يحلّ عنوان بريد بلا جلسة، والعنوان يسكن `persons`؛ فتعداد قراءاته الثلاث في 1.16 لم يعد مغلقاً |
| §4.3-أ/3 | «policies TO authenticator في الـ manifest: قراءة `USING (true)` على جداول الحلّ **الثلاثة**» | أربعة: `persons` ينضم إليها بسياسة `persons_authenticator_read` (§4.1) — جدول حلّ رابع يخدم `forgot` وحده، وحصرُه على العمودين بالـ grant لا بالسياسة (§3) |
| §4.3-أ/4 | «لا يكتب على جداول الهوية نفسها شيئاً» | يكتب `password_reset_tokens` وحده؛ لا يلمس `users` ولا `user_password_credentials` |
| §4.3-ب | `app.user_id` يُضبط من الجلسة المصادَقة | استثناء مسمّى واحد: في `POST /api/auth/password/reset` تأخذ UoW-2 `app.user_id` من صاحب الرمز الذي تحققت منه UoW-1 واستهلكته (§5.1) |
| §4.4/3 | «مساران محصوران اثنان لا غير» | مسار ثالث: الإجراء التشغيلي `src/Ops` بدور `provisioner` (§5.4) |
| §4.4/5 | «لا مسار ثالث يكتب عابراً للعزل» | الإجراء التشغيلي هو الثالث المسمّى: جهة واحدة لكل معاملة، وتدقيقه في نفس المعاملة كالمسارين (§4.4/4) |
| §3.4 (صف `migrator`) | «migrations فقط» | ومعها أمر الاستيراد في `src/Ops.Import` (§6) — أداة ترحيل بيانات لغرض واحد، بشروط §3.4 الثلاثة |
| §3.10 البند أ | «آخر مالك نشط»: لا تبقى جهة بلا مالك نشط | الجهة المُنشأة بـ `create-tenant` بلا مالك نشط حتى تُقبل دعوة `owner` (§5.4)؛ البند أ يسري عليها من أول قبول |
| §4.1 (تعريف `invitations`) | `invited_by` معرّف مستخدم الداعي (`NOT NULL` في المخطط) | `invited_by` يقبل `NULL`، ومعناه «دعوة bootstrap، بلا داعٍ بشري» (§2.11) |

**سلسلة السلطة بعده:** PLATFORM_CORE v1.16 + الملحقات بترتيبها (01، 02…) → PROOF_SPEC v1.3 → الكود → الأدلة. تعارض بين 1.16 وملحق خارج الفقرات التي يسمّيها الملحق معدَّلةً = خطأ في الملحق حتى يقرر صاحب المشروع غير ذلك. يُضاف هذا السطر إلى `CLAUDE.md`.

**ما لا يمسّه (ثابت بنص 1.16):**

- الجهة وحدة العزل الوحيدة (§4.8)؛ المحور الثاني تضييق لا وحدة.
- الأدوار الخمسة، لا عضوية بين أدوار، `BYPASSRLS` للـ migrator وحده، لا `SECURITY DEFINER` (§3.4).
- القالبان القياسيان للسياسات، `NULLIF`، `AS RESTRICTIVE` للتضييق، FK مركّبة بين جدولين بـ `tenant_id` (§3.3).
- التطبيق يولّد كل مفتاح وطابع زمني؛ لا `DEFAULT`/identity/generated (§3.5).
- مبدأ الموافقة: لا عضوية بلا قبول (§0). **الترحيل (§6) لا يخالفه:** المستخدمون المرحَّلون قبلوا عضويتهم في النظام الحي، والترحيل نقل لا استقطاب — فتُنشأ عضوياتهم `active` مباشرةً، بلا دعوة.
- `job_runner` بدوره في 1.16 (§3.4، §8): قراءة الجهات النشطة لبدء الـ fan-out، ولا كتابة.
- الفحوصات العشرة ومدخلاتها؛ ما يضيفه الملحق من منح يدخل `grants.json` في PR الكود. سياسات §4.1 تدخل الـ manifest حين تُبنى (§4.1).

**نطاق الملحق:** ما يلزم لمرحلة «استقبال نظام الاشتراكات» حسب خارطة الطريق المعتمدة — لا أكثر. محرك الفوترة، البوابات، التسجيل الذاتي العام، العلامات/الشركاء، §9، SSO، بوابة العميل: خارجه (§7).

## §1 القرارات المسجّلة

قرارات صاحب المشروع بتاريخ 2026-10-17. كل واحد يقابله بند في `OPEN_ITEMS.md` بالرقم في العمود الأخير: D5 وD8 يحدّثان بندين قائمين، وبند D6 إحالة إلى 36 و16.

| # | القرار | الأثر على 1.16 | OPEN_ITEMS |
| --- | --- | --- | --- |
| D1 | **الخلافة:** المنصة تخلف نظام الاشتراكات الحي (SYSTEM_DESIGN 2.0). تُرحَّل البيانات فقط؛ لا كود يُنقل. القديم للقراءة فقط حتى التبديل، ثم يُطفأ | يحسم الشرط المفتوح في §0 | 42 |
| D2 | **إنشاء الجهات في الإنتاج** بإجراء تشغيلي بدور `provisioner` (bootstrap من القوالب + دعوة `owner` يقبلها المالك). لا تسجيل ذاتي عام | مسار ثالث لـ `provisioner` — **يعدّل §4.4/3 و§4.4/5 و§3.10 البند أ** (§0) | 43 |
| D3 | **قنوات الإرسال:** بريد، واتساب (بوابة مستضافة مثل GreenAPI)، SMS (API)، تلغرام (Bot). بريد + واتساب أولاً. **الخيار أ:** يُبنى داخل M1 قبل التبديل | جداول جديدة (§2)، منح (§3) | 44 |
| D3′ | **ثلاث طبقات للإرسال:** حساب المزوّد للمنصة افتراضياً مع إمكان تجاوز لكل جهة لاحقاً؛ تفعيل القنوات ومواعيدها لكل جهة؛ جهة الاتصال لكل عميل. **أسرار المزوّدين ليست في قاعدة البيانات** (§2.6) | — | 45 |
| D4 | **تدقيق الصفوف المرحَّلة — محسوم 2026-10-17:** **لا قيد `audit_log` للاستيراد**؛ `migrator` لا يصير كاتباً رابعاً (1.16 §7). السجل تقرير أعداد الصفوف الذي يكتبه الأمر خارج قاعدة البيانات، و`legacy_id` للتتبع (§6) | — (§7 كما هو) | 46 |
| D5 | **OPEN_ITEMS 27 يُؤجَّل:** إعادة القياس بعد 10 جهات حقيقية؛ العتبة والبديلان ثابتان | — | 27 |
| D6 | **استعادة كلمة السر وتغييرها** بثلاثة مسارات ووحدتَي عمل (authenticator ثم app_user)، مع `security_stamp` يُنهي كل الجلسات عند تغيير كلمة السر. **يغلق OPEN_ITEMS 36 جزئياً:** نسخة من الكوكي تبقى صالحة بعد الخروج | §2.1، §2.2، §3، §5 — **يعدّل §4.3-أ/2، §4.3-أ/4، §4.3-ب** (§0) | 47 (إحالة إلى 36 و16) |
| D7 | **سجل اشتراك المنصة (الطبقة 1):** خطط بلا أسعار، اشتراك لكل جهة بـ `paid_until`، إيقاف بأمر تشغيلي مجدول، قراءة للمالك والـ admin (`core.billing.read`، §4.2). المحرك والبوابات مؤجلان؛ يُبنيان فوق هذا لا بدله | §2.3، §2.4، §3، §4، §5 | 48 |
| D8 | **OPEN_ITEMS 25 مؤجّل** حتى الوحدة الثانية | — | 25 |
| D9 | **الملحق مستقل** عن 1.16؛ الدمج في 2.0 حين تتراكم الملحقات أو تبدأ الوحدة الثانية | سلسلة السلطة (§0) | 49 |

## §2 الجداول والأعمدة الجديدة (إضافات إلى §4 و§5)

كل جدول يحمل `tenant_id` يتبع القالب القياسي (`tenant_isolation`)، ومن يحمل `scope_ref_id` يتبع القالبين. المفاتيح `app_id` والطوابع `app_ts` كما في 1.16. أسماء الأعمدة مبدئية حتى الترحيل.

### 2.1 `users.security_stamp` — عمود جديد (إلى §4.2)

`security_stamp app_id NOT NULL`. يولّده التطبيق عند إنشاء المستخدم ويغيّره عند كل تغيير لكلمة السر. يدخل الكوكي؛ `UnitOfWorkMiddleware` يقارنه مع الصف في كل طلب (قراءة واحدة تحت `user_self_read` الموجودة)؛ اختلافه = جلسة منتهية (401). كتابته عبر `user_self_update` كتابة هوية ذاتية، فتنضم إلى قائمة الاستثناء من التدقيق التلقائي (1.16 §7) بجوار `last_login_at, language, theme` — فلا أثر تدقيق لتغيير كلمة السر حتى يوجد سجل تدقيق الهوية (OPEN_ITEMS 52). **الترحيل:** عمود `NOT NULL` على جدول فيه صفوف → backfill بقيمة لكل صف داخل نفس الترحيل، ثم فحص عدد الصفوف (1.16 §3.4، الشرط 2).

### 2.2 `password_reset_tokens` — جدول عام (إلى §4.2، بجوار `auth_attempts`)

```sql
password_reset_tokens(id, user_id → users, token_hash, requested_at, expires_at, used_at NULL, request_ip NULL)
-- بلا tenant_id. RLS مفعّل. يخزَّن hash الرمز فقط (SHA-256 لرمز 32 بايت).
-- طلب جديد يضع used_at على الرموز السابقة غير المستخدمة للمستخدم نفسه.
-- الاحتفاظ: نفس سياسة auth_attempts (بند §13 في 1.16) — تنظيف تشغيلي بدور migrator.
-- كتابات authenticator عليه كتابات هوية لا بيانات جهة: خارج التدقيق التلقائي كـ auth_attempts (1.16 §7)،
-- و token_hash في قائمة الحجب أصلاً.
```

### 2.3 `plans` — كتالوج عام (إلى §5)

`plans(id, code, name_ar, name_en, is_active, UNIQUE (code))`. يزرعه `migrator` فقط. **لا أسعار** — السعر اتفاق خارج المنصة في هذه المرحلة. الخطط الأولى: [`trial`, `standard`] — افتراض.

### 2.4 `platform_subscriptions` — لكل جهة (إلى §4.1)

```sql
platform_subscriptions(id, tenant_id, plan_code → plans(code), status, period_start, paid_until date, grace_days, created_at, updated_at,
  UNIQUE (tenant_id))                       -- صف واحد لكل جهة في هذه المرحلة
-- paid_until تاريخ (date) بتوقيت UTC؛ suspend-unpaid يقارن بـ (now() AT TIME ZONE 'UTC')::date (§5.4) — لا بـ current_date، الذي يتبع TimeZone الجلسة. موعد بلا منطقة زمنية خلل ينتظر جهة في بلد آخر.
-- status ∈ {active, suspended} — المهلة يحملها grace_days، فلا حالة وسيطة. القالب القياسي.
-- يُنشأ في bootstrap (provisioner) بخطة trial وpaid_until = [اليوم + 30] — افتراض.
-- تتغير paid_until/status بالإجراء التشغيلي (provisioner) وحده: mark-paid وsuspend-unpaid (§5.4).
```

**الحدّ الصريح:** هذا ليس فاتورة ولا دفعة. لا `invoices` ولا `payments` في هذا الملحق.

### 2.5 `tenant_notification_settings` — لكل جهة (إلى §4.1؛ ملكيته الوحدة)

`(id, tenant_id, channel, is_enabled, reminder_days_before int[], sender_display_name, created_at, updated_at, UNIQUE (tenant_id, channel))`. `channel ∈ {email, whatsapp, sms, telegram}`. القالب القياسي. تُنشأ الصفوف عند تفعيل الوحدة للجهة بقيم من النظام الحي [—].

### 2.6 `provider_accounts` — المنصة مع تجاوز اختياري للجهة (إلى §4.1)

```sql
provider_accounts(id, tenant_id NULL, channel, provider_code, sender_id, credential_ref, is_active, created_at,
  UNIQUE NULLS NOT DISTINCT (tenant_id, channel))   -- وصفّ المنصة: tenant_id IS NULL، واحد لكل قناة
-- credential_ref = اسم المفتاح في إعدادات Worker/Api (env/secret store). **لا سرّ في قاعدة البيانات.**
-- في هذه المرحلة تُزرع صفوف المنصة فقط (migrator). صفوف الجهات: باب محجوز، لا مسار يكتبها.
```

لماذا: `app_user` يقرأ هذا الجدول في كل معاملة إرسال؛ لو حمل مفاتيح المزوّدين لكان كل عضو جهة على بعد ثغرة واحدة من مفاتيح المنصة. المرجع إلى الإعدادات يُبقي السرّ حيث تعيش سلاسل الاتصال. **`NULLS NOT DISTINCT`:** بدونه لا يمنع `UNIQUE` صفّين للمنصة لنفس القناة، لأن `NULL` لا يساوي `NULL`. **سياسة القراءة** تحتاج صياغة خارج القالب: `tenant_id = app.tenant_id OR tenant_id IS NULL` — سياسة مُسمّاة تدخل الـ manifest (Check 2) حين تُبنى (§4.1).

### 2.7 `notification_log` — لكل جهة ومُنطَّق (إلى الوحدة، بجوار `subscriptions`)

```sql
notification_log(id, tenant_id, subscription_id, scope_ref_id NOT NULL, channel, reminder_day date, provider_message_id NULL, status, attempted_at, sent_at NULL, error NULL,
  FOREIGN KEY (tenant_id, subscription_id) REFERENCES subscriptions (tenant_id, id),
  UNIQUE (tenant_id, subscription_id, channel, reminder_day))   -- مفتاح عدم التكرار
-- reminder_day = تاريخ الإرسال المخطط.
-- scope_ref_id = عميل الاشتراك → القالبان، وNOT NULL (قاعدة 1.8).
-- status ∈ {pending, sent, failed}.
-- يُكتب pending في معاملة الحساب (outbox)؛ خطوة الإرسال تنقله إلى sent|failed (§5.5).
-- لا DELETE لأي دور تطبيقي؛ الحالة تتقدم فقط.
```

### 2.8 أعمدة على `clients` (إلى الوحدة)

`email NULL`, `phone_e164 NULL`, `telegram_chat_id NULL`, `is_active NOT NULL` (تعطيل لا حذف). كلها بنفس منح `clients` الحالية. **`is_active` على جدول فيه صفوف** → نفس قاعدة §2.1: backfill لكل صف داخل نفس الترحيل، ثم فحص عدد الصفوف.

### 2.9 أعمدة على `subscriptions` (إلى الوحدة)

`status ∈ {active, cancelled, renewed}` (`NOT NULL`، بنفس قاعدة backfill في §2.1)، `renewed_from_id NULL → subscriptions(tenant_id, id)` (مركّب). التجديد = اشتراك جديد يشير إلى القديم؛ الإلغاء = تغيير حالة. **لا DELETE** — يبقى كما في 1.16 للوحدات لكن الوحدة لا تستعمله.

### 2.10 `legacy_id` — مؤقت

عمود `legacy_id text NULL` على `clients` و`subscriptions` و`tenants` و`users` يضيفه ترحيل مخطط مرقّم قبل الاستيراد، ويملؤه أمر الاستيراد (§6)، ويحذفه ترحيل لاحق بعد التبديل. وجوده بعد الإطفاء خطأ.

### 2.11 `invitations.invited_by` يقبل `NULL` (يعدّل تعريف §4.1 — §0)

`NULL` = دعوة bootstrap، بلا داعٍ بشري: دعوة `owner` التي يكتبها `create-tenant` (§5.4) لجهة لا مستخدم فيها بعد. كل دعوة أخرى تحمل داعيها: `invitations_insert` (1.16 §3.9) تشترط `invited_by = app.user_id` فلا يكتب `app_user` دعوة بلا داعٍ، و`invitations_provisioner_insert` (§4.1) تشترط `invited_by IS NULL` فلا يزوّر مسار `provisioner` داعياً.

### 2.12 المستخدم المرحَّل بلا كلمة سر — افتراض موسوم

الاستيراد (§6/3) يُنشئ لكل مستخدم مُرحَّل صفّ `user_password_credentials` في حالة «بلا كلمة سر»: **لا يطابقه أي إدخال، ويرفضه الـ verifier دون أن يرمي.** هذه قاعدة لا تنفيذ؛ وهي **افتراض يُثبَت على PostgreSQL 18 في PR الكود** قبل البناء عليه، كبقية الافتراضات الموسومة.

## §3 مصفوفة المنح كاملة محدّثة (تحل محل §3.8 عند الدمج)

الأسطر من 1.16 منقولة حرفياً. الجديد **بالخط العريض** ومعلَّم بـ «(الملحق 01)». ما ليس في المصفوفة ليس ممنوحاً. تُنقل إلى `checks/core/grants.json` في PR الكود، ويفشل Check 6 حتى تتطابق.

| الجدول | app_user | authenticator | provisioner | job_runner |
|---|---|---|---|---|
| tenants | SELECT | — | INSERT، **SELECT، UPDATE (status) (الملحق 01 — §5.4)** | SELECT |
| persons | SELECT | **SELECT (أعمدة: id, email فقط) (الملحق 01 — §5.1)** | **SELECT**، INSERT | — |
| users | SELECT، UPDATE (أعمدة: last_login_at, language, theme فقط) **+ security_stamp (الملحق 01 — §2.1)** | SELECT | **SELECT**، INSERT | — |
| user_password_credentials | UPDATE (password_hash, updated_at — **1.12**) **بلا SELECT** | SELECT | INSERT | — |
| memberships | SELECT، UPDATE (status) — **والسياسات تحصر الانتقالات (1.16، 3.10)** | — | **SELECT**، INSERT، **UPDATE (status) — العودة وحدها (1.16، D9)** | — |
| membership_roles | SELECT، INSERT، DELETE — **بصلاحية core.members.manage، لا على النفس (1.16)** | — | INSERT، **SELECT، DELETE (1.16 — العودة وحدها)** | — |
| membership_auth | SELECT | SELECT | INSERT | — |
| membership_scope | SELECT، UPDATE (scope_mode) — **والـ policy تشترط `can_manage_scope` وتمنع تعديل عضوية النفس** (4.8) | — | INSERT، **SELECT، UPDATE (scope_mode) (1.16 — العودة وحدها)** | — |
| scope_assignments | SELECT، INSERT، UPDATE (active) — **لا DELETE**: الإسناد تاريخ يُعطَّل ولا يُمحى | — | **SELECT، UPDATE (active) (1.16 — تعطيل عند العودة وحده)** | — |
| invitations | SELECT، INSERT، UPDATE (status) | — | **INSERT (الملحق 01 — دعوة `owner` في `create-tenant`، §5.4)**، SELECT، UPDATE (status) | — |
| auth_attempts | — | SELECT، INSERT | — | — |
| **password_reset_tokens (الملحق 01)** | — | **SELECT، INSERT، UPDATE (used_at)** | — | — |
| audit_log | SELECT (**بشرط `scope_all` — 1.9، القسم 7**)، INSERT — **لا UPDATE/DELETE لأي دور تطبيقي** | — | INSERT | — |
| roles | SELECT، INSERT، UPDATE، DELETE (ضمن RLS؛ **والأدوار المزروعة is_system محصّنة بـ policy مقيِّدة من القاعدة — 1.8، القسم 5**) | — | SELECT، **INSERT (1.13 — الأدوار الأساسية وحدها، bootstrap)** | — |
| role_permissions | SELECT، INSERT، DELETE | — | **INSERT (1.13 — bootstrap)** | — |
| tenant_modules | SELECT، UPDATE (is_active) | — | INSERT | — |
| modules (كتالوج عام) | SELECT فقط — الكتابة لـ migrator (زرعاً) | — | SELECT | — |
| permissions (كتالوج عام) | SELECT فقط — الكتابة لـ migrator (زرعاً) | — | SELECT | — |
| role_templates (كتالوج عام — 1.13) | — | — | SELECT فقط — الكتابة لـ migrator (زرعاً) | — |
| role_template_permissions (كتالوج عام — 1.13) | — | — | SELECT فقط — الكتابة لـ migrator (زرعاً) | — |
| **plans (كتالوج عام — الملحق 01)** | **SELECT فقط — الكتابة لـ migrator (زرعاً)** | — | **SELECT** | **SELECT** |
| **platform_subscriptions (الملحق 01)** | **SELECT** (بصلاحية `core.billing.read` في المسار) | — | **INSERT، SELECT، UPDATE (status, paid_until, period_start, plan_code)** | — |
| **provider_accounts (الملحق 01)** | **SELECT** — الكتابة لـ migrator (زرعاً) | — | — | — |
| **tenant_notification_settings (الملحق 01)** | **SELECT، INSERT، UPDATE (is_enabled, reminder_days_before, sender_display_name)** | — | **INSERT** (تهيئة الوحدة عند bootstrap) | — |
| **notification_log (الملحق 01)** | **SELECT، INSERT، UPDATE (status, provider_message_id, error, sent_at)** — **لا DELETE**؛ الحالة تتقدم فقط (outbox) | — | — | — |
| جداول الموديولات | SELECT، INSERT، UPDATE، DELETE (ضمن RLS) | — | — | — |

### تبرير الأسطر الجديدة

- **`provisioner` على `tenants` (SELECT، UPDATE status):** الإيقاف وإرجاع الجهة الموقوفة بالإجراء التشغيلي (§5.4)، جهة واحدة لكل معاملة، وسياساته على الصفّ `id = app.tenant_id` (§4.1). SELECT لأن `UPDATE … WHERE` يقرأ الصف. هذا السطح هو المسار الثالث المسمّى في §0 (يعدّل §4.4/3 و§4.4/5).
- **`provisioner` على `invitations` (INSERT):** `create-tenant` يكتب دعوة `owner` للجهة الجديدة، و`invited_by IS NULL` — دعوة bootstrap بلا داعٍ بشري (§2.11)، لا مستخدم جلسة أبداً. الجهة بلا مالك نشط حتى تُقبل الدعوة (يعدّل §3.10 البند أ — §0).
- **`job_runner` على `plans` (SELECT):** قراءة كتالوج عام. القرار 1 أزال كتابات `job_runner` لا قراءاته؛ ومهمة لاحقة تقرأ الكتالوج لا تعيد فتح المصفوفة.
- **`authenticator` على `password_reset_tokens`:** الاستعادة تبدأ بلا جلسة، وهو الدور الوحيد الذي يعمل بلا جلسة. **هذا يعدّل §4.3-أ/2 و§4.3-أ/4** (§0)، لا يضيف إليهما. الكتابة محصورة في الرمز نفسه؛ لا يلمس كلمة السر — التحديث يتم كـ `app_user` بسياق المستخدم صاحب الرمز عبر `password_self_update` الموجودة (استثناء §4.3-ب، §0).
- **`authenticator` على `persons` (SELECT على `id` و`email` وحدهما):** `forgot` يبدأ بعنوان بريد ولا جلسة بعد، والعنوان يسكن `persons` وحده. **ولا دور آخر يصلح لحلّ بلا جلسة:** `app_user` لا يرى إلا صفّه عبر `person_self`، وهي مشروطة بـ `app.user_id` الذي لا وجود له قبل المصادقة؛ وكل سياسات `provisioner` مشروطة بـ `app.tenant_id` ولا جهة معروفة قبل الحلّ، و`provisioner` مسار محصور بلا HTTP (§4.4)؛ و`job_runner` بلا منح على `persons` أصلاً؛ و`migrator` ممنوع على التطبيق (§3.4). فيبقى `authenticator`، الدور الوحيد الذي يعمل بلا جلسة (§4.3-أ). **هذا يعدّل §4.3-أ/2 و§4.3-أ/3** (§0)، لا يضيف إليهما؛ **و§4.3-أ/4 يبقى على حاله**: قراءة لا كتابة، فلا يكتب على جداول الهوية نفسها شيئاً. والقراءة محصورة بالعمودين: لا اسم ولا هاتف — `persons.email` معرّف هوية فريد عالمياً بنصّ 1.16 §3.3، وتسرّب وجوده مقبول ومعالَج بردّ موحّد، أما بقية PII فيمنعها المنح العمودي لا السياسة.
  **وخطر مقبول مُعلَن:** السياسة `USING (true)`، فالدور يقرأ **كل** صفوف `persons`، لا الصفّ المطلوب وحده — المنح العمودي يضيّق الأعمدة لا الصفوف. وسياسة تضيّق الصفوف غير قابلة للتعبير هنا: الصف المقصود لا يُعرف إلا بعد البحث الذي هي نفسها تحكمه، ولا متغيّر جلسة يعرّفه قبله. فاختراق `authenticator` = تعداد كل عنوان نحتفظ به، و`persons.email` فريد عالمياً (§3.3). **مقبول** لأن مسار الدخول قائم على الأرض نفسها منذ 1.16 — `users` و`user_password_credentials` و`membership_auth` كلها `USING (true)` لهذا الدور — وهذا جدول حلّ رابع لا نوع خطر جديد. والتخفيف هو حصر الدور نفسه: اتصال ودور منفصلان، بلا جلسة، وسطح محصور بنائياً بمسار المصادقة (§4.3-أ/1). مسجَّل بند `OPEN_ITEMS` 56.
- **`app_user` على `provider_accounts` (SELECT فقط):** المهمة الخلفية تعمل لكل جهة كـ `app_user` (1.16 §8)، وتحتاج معرفة أي مزوّد. لا أسرار في الجدول (§2.6).
- **`notification_log` (outbox):** المهمة تكتب السطر `pending` وتغلق المعاملة؛ خطوة الإرسال تحدّثه إلى `sent|failed` في معاملة قصيرة. لا DELETE؛ سياسة مقيِّدة تسمح بالانتقال `pending → sent|failed` فقط. الإعادة = سطر جديد.
- **`provisioner` على `tenant_notification_settings` (INSERT):** يهيئ الإعدادات الافتراضية مع bootstrap كما يهيئ الأدوار من القوالب. لا SELECT: لا يحتاج قراءتها.

**ما لم يُمنح عمداً:** `app_user` لا يكتب `platform_subscriptions` (لا جهة تمدّد اشتراكها بنفسها)؛ `job_runner` لا يكتب شيئاً — دوره في 1.16 كما هو، وجديده الوحيد قراءة الكتالوج `plans`؛ لا دور جديد.

## §4 السياسات الجديدة والصلاحيات الجديدة

### 4.1 سياسات خارج القالبين (تدخل الـ manifest لـ Check 2 حين تُبنى)

كل سياسة بـ `FOR` و`TO` صريحين و`NULLIF` على `current_setting`، والنصّ أدناه هو النصّ الذي يُستخرج إلى الـ manifest حرفياً. الجدولان اللذان يدخلان الـ manifest بسياسة خارج القالبين (`tenant_notification_settings`، `notification_log`) يكتبان قالبيهما صراحةً، لأن مجموعة سياسات جدول الـ manifest تُفحص كاملة. و`persons` ليس منها: مجموعته في الـ manifest مكتوبة في 1.16 كاملةً، وسياسة الملحق تنضم إليها.

```sql
-- ===== authenticator — حلّ العنوان بلا جلسة (§5.1) =====
CREATE POLICY persons_authenticator_read ON persons
  FOR SELECT TO authenticator
  USING (true);   -- الحصر على العمودين (id, email) بالـ grant لا بالسياسة؛ والسطح محصور بنائياً بمسار المصادقة

-- ===== authenticator — رموز الاستعادة (§2.2) =====
CREATE POLICY password_reset_tokens_authenticator_select ON password_reset_tokens
  FOR SELECT TO authenticator
  USING (true);   -- الدور يرى الرموز كلها؛ المطابقة بالـ hash في الاستعلام

CREATE POLICY password_reset_tokens_authenticator_insert ON password_reset_tokens
  FOR INSERT TO authenticator
  WITH CHECK (true);

CREATE POLICY password_reset_tokens_authenticator_use ON password_reset_tokens
  FOR UPDATE TO authenticator
  USING (used_at IS NULL)
  WITH CHECK (used_at IS NOT NULL);   -- الكتابة الوحيدة هي الاستهلاك

-- ===== الكتالوج (§2.3) =====
CREATE POLICY plans_read ON plans
  FOR SELECT TO app_user, provisioner, job_runner
  USING (true);

-- ===== platform_subscriptions (§2.4) =====
CREATE POLICY platform_subscriptions_read ON platform_subscriptions
  FOR SELECT TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));   -- شرط القالب، FOR SELECT وحده: لـ app_user منح SELECT فقط

CREATE POLICY platform_subscriptions_provisioner_insert ON platform_subscriptions
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY platform_subscriptions_provisioner_select ON platform_subscriptions
  FOR SELECT TO provisioner
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY platform_subscriptions_provisioner_update ON platform_subscriptions
  FOR UPDATE TO provisioner
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

-- ===== provisioner — الإجراء التشغيلي (§5.4) =====
CREATE POLICY tenants_provisioner_select ON tenants
  FOR SELECT TO provisioner
  USING (id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY tenants_provisioner_update ON tenants
  FOR UPDATE TO provisioner
  USING (id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY invitations_provisioner_insert ON invitations
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
              AND status = 'pending'
              AND invited_by IS NULL);   -- دعوة bootstrap: لا يزوّر مسار provisioner داعياً (§2.11)

-- ===== الإرسال (§2.5–§2.7) =====
CREATE POLICY provider_accounts_read ON provider_accounts
  FOR SELECT TO app_user
  USING (tenant_id IS NULL
         OR tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));   -- صفّ المنصة أو صفّ الجهة الحالية (§2.6)

CREATE POLICY tenant_isolation ON tenant_notification_settings
  FOR ALL TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY tenant_notification_settings_provisioner_insert ON tenant_notification_settings
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY tenant_isolation ON notification_log
  FOR ALL TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY client_scope ON notification_log
  AS RESTRICTIVE FOR ALL TO app_user
  USING (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = notification_log.tenant_id
        AND sa.scope_ref_id  = notification_log.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active))
  WITH CHECK (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = notification_log.tenant_id
        AND sa.scope_ref_id  = notification_log.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active));

CREATE POLICY notification_log_status_forward ON notification_log
  AS RESTRICTIVE FOR UPDATE TO app_user
  USING (status = 'pending')
  WITH CHECK (status IN ('sent', 'failed'));   -- الحالة تتقدم فقط: pending → sent|failed
```

**الإنفاذ:** `checks/core/generate-manifest.py` يقرأ 1.16 ثم ملحقاتها بترتيب أرقامها، ويشترط أن يتطابق نصّ كل `CREATE POLICY` في العربي والترجمة (بلا التعليقات)؛ والنصّ الإنجليزي هو ما يدخل الـ manifest. **سياسة من هذا القسم تدخل الـ manifest حين تُبنى:** PR الكود الذي يُنشئها يضيف اسمها (`table.policy`) إلى `checks/core/amendment-policies-built.json` ويعيد توليد الـ manifest في الـ PR نفسه؛ وما لم يُبنَ بعد يُذكر «pending» ولا يُطلب من Check 2 ولا Check 3. واسم يدخل ذلك الملف لا يخرج منه إلا بقرار صريح من صاحب المشروع يُسجَّل في الـ PR. والفحص المحلي `check-manifest-extraction` يرفض manifest لا يطابق الاستخراج.

### 4.2 صلاحيات جديدة (إلى §5)

| الكود | الوحدة | المعنى | القوالب |
| --- | --- | --- | --- |
| `core.billing.read` | core | قراءة اشتراك المنصة للجهة الحالية | owner, admin |
| `subscriptions.manage_settings` | subscriptions | تعديل قنوات التنبيه ومواعيدها | owner, admin |
| `subscriptions.clients.write` | subscriptions | إنشاء/تعديل عميل | owner, admin, operator |
| `subscriptions.clients.manage` | subscriptions | تعطيل عميل وإعادة تفعيله | owner, admin |
| `subscriptions.notifications.read` | subscriptions | قراءة سجل الإرسال | owner, admin, operator, viewer |

الإدخال بنفس وصفة الدليل §11: الصلاحية، القوالب، ترقية أدوار الجهات القائمة، فحص عدد الصفوف — في ترحيل واحد.

### 4.3 ما يبقى بلا صلاحية جديدة

`subscriptions.write` تغطي إنشاء الاشتراك وتجديده وإلغاءه. استعادة كلمة السر وتغييرها بلا صلاحية — مسار هوية لا مسار جهة.

## §5 المسارات: endpoints، المهام الخلفية، الإجراء التشغيلي

### 5.1 endpoints جديدة (إلى مرجع الدليل §14)

| المسار | النوع | الصلاحية | ملاحظة |
| --- | --- | --- | --- |
| `POST /api/auth/password/forgot` | own UoW (authenticator) | — | 204 دائماً؛ حدّ معدل (§5.2)؛ بريد عبر المُرسِل. الـ 204 موحّد وزمن الرد ليس موحّداً (OPEN_ITEMS 51) |
| `POST /api/auth/password/reset` | own UoW ×2 | — | UoW-1 authenticator يتحقق من الرمز ويستهلكه؛ UoW-2 app_user (`app.user_id` = صاحب الرمز — الاستثناء المسمّى على §4.3-ب، §0) يحدّث الـ hash ويغيّر `security_stamp`؛ لا كوكي يُصدر |
| `PUT /api/me/password` | own UoW ×2 | session | التحقق من الحالية (authenticator) ثم التحديث (app_user)؛ الكوكي يُعاد إصداره بالختم الجديد |
| `GET /api/tenant/subscription` | normal | `core.billing.read` | الخطة، الحالة، `paid_until` |
| `GET/PUT /api/subscriptions/settings` | module | `subscriptions.read` / `subscriptions.manage_settings` | قنوات الجهة ومواعيدها |
| `POST /api/subscriptions/clients`, `PUT …/{id}` | module | `subscriptions.clients.write` | عميل: إنشاء، تعديل |
| `PUT /api/subscriptions/clients/{id}/status` | module | `subscriptions.clients.manage` | عميل: تعطيل، إعادة تفعيل |
| `POST /api/subscriptions/{id}/renew`, `POST …/{id}/cancel` | module | `subscriptions.write` | التجديد اشتراك جديد بـ `renewed_from_id` |
| `GET /api/subscriptions/notifications` | module | `subscriptions.notifications.read` | ScopedList |

لا مسار يقرأ `provider_accounts` أو يكتب `platform_subscriptions` من الـ API.

### 5.2 الدخول — تغييران

- **`security_stamp`** يدخل الكوكي؛ `UnitOfWorkMiddleware` يقارنه في كل طلب مع `users` (تحت `user_self_read`). اختلاف → 401 `session_expired` (كود جديد، وصفة §14).
- **حدّ المحاولات** — بالاتجاه المسجّل في OPEN_ITEMS 16، لا بغيره: `Authenticator` يقرأ `auth_attempts` الفاشلة لنفس (`username` + `ip`) ويؤخّر الرد **تأخيراً متصاعداً**، **بلا قفل حساب** (القفل يتيح لأي أحد قفل مستخدم معروف)، مع **سقف لكل `ip`** يُرفض فوقه بـ 429 `too_many_attempts` **قبل** قراءة كلمة السر. الأرقام (التأخيرات، النوافذ، السقف) [—] تُحسم عند الإطلاق. نفس الآلية لـ `forgot` بـ (البريد + `ip`).

### 5.3 المهام الخلفية (إلى §8)

| المهمة | الدور ثم السياق | ماذا تفعل |
| --- | --- | --- |
| `ExpiringSubscriptionsJob` (موجودة، تُوسَّع) | job_runner يسرد الجهات → app_user لكل جهة بسياق نظام | تحسب المستحق لكل `reminder_days_before` وتكتب `notification_log` بحالة `pending` وتُغلق المعاملة؛ خطوة ثانية ترسل وتنقل كل سطر إلى `sent`/`failed` في معاملة قصيرة (outbox — §5.5)؛ فشل قناة = سطر `failed` ولا يوقف غيرها؛ فشل جهة لا يوقف البقية |

**الإيقاف ليس مهمة خلفية:** هو أمر `suspend-unpaid` في الإجراء التشغيلي بدور `provisioner` (§5.4). `job_runner` يبقى بدوره في 1.16: يقرأ الجهات النشطة ولا يكتب.

### 5.4 الإجراء التشغيلي (D2)

`src/Ops` — مشروع يحمل سلسلتَي `provisioner` و`job_runner` وحدهما، بلا مسار HTTP. **هو المسار الثالث لـ `provisioner` (يعدّل §4.4/3 و§4.4/5 — §0):** جهة واحدة لكل معاملة بـ `app.tenant_id` مضبوطاً عليها، وكل أمر فيه كتابة يسجّل قيد `audit_log` في نفس معاملته كأي عملية `provisioner` (§4.4/4)؛ وما لا يكتب لا يسجّل. أوامره:

- `create-tenant --name --owner-email [--plan trial]`: bootstrap من القوالب + `platform_subscriptions` + `tenant_notification_settings` الافتراضية + دعوة `owner` بالبريد (`invited_by IS NULL` — دعوة bootstrap بلا داعٍ بشري، §2.11). المالك يقبل بنفسه؛ والجهة بلا مالك نشط حتى القبول (§0، §3.10 البند أ).
- `mark-paid --tenant --until [--plan]`: يحدّث `paid_until` و`platform_subscriptions.status` إلى `active`، و`tenants.status` إلى `active`. الطريقة الوحيدة لإرجاع جهة موقوفة.
- `suspend-unpaid`: يُجدوَل من خارج المنصة. يسرد الجهات النشطة بدور `job_runner` عبر `tenants_for_jobs` (قراءة فقط — دوره في 1.16)، ثم لكل جهة معاملة `provisioner` مستقلة: `paid_until + grace_days < (now() AT TIME ZONE 'UTC')::date` (§2.4) → `platform_subscriptions.status = suspended` و`tenants.status = suspended`، كتابتان حرجتان (`CriticalWrite`) تحت سياسات §4.1. فشل جهة لا يوقف البقية.
- `announce-migration`: يُشغَّل مرة واحدة **بعد التبديل (M3)**، لا قبله ولا أثناء الاستيراد. يرسل لكل مستخدم مُرحَّل (`users.legacy_id IS NOT NULL`) بريد إعلان عادياً يحمل رابطاً إلى صفحة «نسيت كلمة السر» (§6/7). قراءة فقط — `persons` و`users` بمنح `provisioner` القائمة — ولا كتابة؛ والمُرسِل وإعداداته (`credential_ref`) في `src/Ops` لا في `src/Ops.Import`.
- `src/Ops` يرفض التشغيل إن وجد سلسلة `migrator` في بيئته (نفس حارس Api). أمر الاستيراد في مشروع منفصل، `src/Ops.Import`، يحمل `migrator` وحده (§6).

الإيقاف أثره فوري بطبيعة 1.16: `POST /tenants/{id}/select` يرفض الجهة الموقوفة في الطلب التالي.

### 5.5 المُرسِل

`INotificationSender` في `Core` (ليس في الوحدة — الدعوات وكلمة السر تستعملانه). تنفيذ لكل قناة؛ الاختيار من `provider_accounts` ثم `credential_ref` من الإعدادات. **نمط outbox (قرار 2026-10-17):** لا إرسال داخل معاملة الحساب. (1) المهمة تحسب وتكتب `notification_log` بحالة `pending` وتُغلق المعاملة — الـ UNIQUE يمنع تكرار السطر هنا. (2) خطوة الإرسال في نفس التشغيل تقرأ `pending`، تستدعي المزوّد بمهلة 5 ث، وتحدّث السطر إلى `sent` (مع `provider_message_id`) أو `failed` (مع `error`) في معاملة قصيرة لكل سطر. سطر `pending` بقي بعد التشغيل = إعادة محاولة في التشغيل التالي، لا سطر جديد. الدعوات وكلمة السر تستعمل المُرسِل مباشرةً (طلب واحد، لا دفعة).

**التسليم مرة على الأقل (at-least-once)، لا مرة واحدة بالضبط:** الـ UNIQUE يمنع تكرار **السطر** لا تكرار **الإرسال** — إن قَبِل المزوّد الرسالة ولم يُحفظ `sent`، بقي السطر `pending` وأُرسل ثانيةً في التشغيل التالي. وتحديث الحالة كتابة حرجة (`CriticalWrite`): صفر صفوف متأثرة = خطأ يُرمى، لا نجاح صامت.

## §6 الترحيل من النظام الحي — قواعد ملزمة

تطبيق لشروط 1.16 §3.4 (استثناء `BYPASSRLS`) على حالة الاستيراد الوحيدة المسموح بها. **الاستيراد أمر في مشروع منفصل `src/Ops.Import` بدور `migrator`، لا ترحيل مرقّم:** الترحيل المرقّم يعمل في CI وdev وكل بيئة، فكان سيحتاج بيانات الإنتاج في المستودع.

**شروط 1.16 §3.4 الثلاثة على هذا الأمر:**

- **الشرط 1 — اتصال `migrator` لأداة ترحيل وحدها:** أمر `import-legacy` في `src/Ops.Import` أداة ترحيل بيانات لغرض واحد موثّق هنا، يحمل سلسلة `migrator` وحدها، ولا يصله Api ولا Worker ولا `src/Ops` (يعدّل صف `migrator` في §3.4 — §0).
- **الشرط 2 — أعداد الصفوف:** البند 4 أدناه.
- **الشرط 3 — البديلان المرفوضان:** (أ) **ترحيل مرقّم في `src/Migrations`** — رُفض: يعمل في كل بيئة، فإما بيانات إنتاج في المستودع وإما ترحيل يفشل في CI. (ب) **الاستيراد عبر مسار `provisioner` جهةً جهة** — رُفض: `provisioner` لا منح له على جداول الوحدة (`clients`، `subscriptions`، `notification_log`) ولا على `legacy_id`، وفتحها له يوسّع سطحه الدائم من أجل تشغيل واحد؛ و`migrator` محصور بأداة تُشغَّل مرة.

1. **بيانات لا كود** (D1). المدخل تصدير CSV/JSON من النظام الحي يُعطى للأمر من خارج المستودع؛ لا بيانات إنتاج في المستودع. الأمر يُشغَّل على الإنتاج مرة واحدة، بعد التجربة الجافة (8).
2. **كل مفتاح وطابع من التطبيق:** يُكتبان صراحةً في كل صف؛ لا `DEFAULT` ولا `RETURNING`؛ `legacy_id` يُحفظ للمطابقة.
3. **خريطة ثابتة:** حساب حي → `tenants` + `platform_subscriptions`(active، `paid_until` = [—]) + `tenant_notification_settings`؛ مستخدم حي → `persons` + `users` (`security_stamp` جديد) + `user_password_credentials` (في حالة «بلا كلمة سر» — §2.12 — فتبقى `password_self_update` صالحة ولا يحتاج `provisioner` منحاً جديداً) + `memberships`(**active مباشرةً، بلا دعوة** — §0، مبدأ الموافقة) + `membership_roles`(المالك يُسمّى صراحةً في ملف التصدير، لا يُستنتج من الترتيب: [—]؛ وأدوار البقية [—]) + `membership_scope`(`all` ما لم يُحدد غيره — غياب الصف خطأ صاخب في 1.16)؛ عميل حي → `clients`؛ اشتراك حي → `subscriptions`(active).
4. **ينتهي بفحوص عدد الصفوف** لكل جدول مقابل التصدير، وبفحص: لكل `subscriptions` صف `clients` بنفس `tenant_id` (الـ FK المركّب يضمنه، والفحص يسمّيه).
5. **التدقيق** حسب D4: **لا قيد `audit_log` للاستيراد** — `migrator` ليس كاتب تدقيق (1.16 §7). السجل تقرير أعداد الصفوف الذي يكتبه الأمر خارج قاعدة البيانات (البند 4)، و`legacy_id` للتتبع.
6. **لا إرسال أثناء الترحيل:** `notification_log` يُزرع بصفوف `sent` لما أرسله النظام القديم خلال آخر [30] يوماً — وإلا أرسلت المنصة تذكيراً ثانياً أول ليلة. هذا الصف هو الحامي الوحيد من التكرار.
7. **الدخول بعد الترحيل:** **الاستيراد لا يرسل شيئاً**، ولا يُصدر رموزاً، ولا يستدعي `forgot`: يُنشئ صفّ «بلا كلمة سر» (البند 3، §2.12) ويتوقف. الإعلان خطوة لاحقة منفصلة: أمر `announce-migration` في `src/Ops` (§5.4)، بعد التبديل (M3) وحده — فلا يصل بريد والقديم ما زال الحي، ولا يحمل `src/Ops.Import` إعدادات مزوّد. يرسل لكل مستخدم مُرحَّل بريد إعلان عادياً يحمل رابطاً إلى صفحة «نسيت كلمة السر»، وكل مستخدم يبدأ المسار بنفسه (§5.1) — نفس الرمز ونفس عمره؛ لا نوع رمز جديد ولا عمر جديد. فلا يبلغ الإعلان سقف `ip` في §5.2، وعمر الرمز (30 د) يبدأ من طلب المستخدم نفسه لا من الترحيل. ثم يعيّنها بـ `POST /api/auth/password/reset`، الذي يحدّث صف `user_password_credentials` المُنشأ في البند 3. لا دعوة (العضوية موجودة، فلا تصادم مع `UNIQUE (tenant_id, user_id)`)، ولا hashes تُنقل.
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
5. **الإرسال داخل المعاملة** (§5.5): مقبول أن يطول COMMIT بمقدار مهلة المزوّد؟ البديل (outbox + مهمة إرسال) أصحّ هندسياً وأثقل بناءً — اقتُرح للملحق 02 إن ظهرت مشكلة.
6. **`provider_accounts` للمنصة**: يزرعه migrator (ثابت في الترحيل) أم الإجراء التشغيلي؟

**الإجابات المعتمدة:**

1. **D4** (عُدّل 2026-10-17، كان قيداً واحداً لكل جهة): لا قيد `audit_log` للاستيراد؛ تقرير أعداد الصفوف خارج قاعدة البيانات، و`legacy_id` للتتبع (§6/5).
2. **الحدود:** 30 د للرمز؛ 5 ث للمزوّد؛ `grace_days` 7؛ `trial` 30 يوماً — كلها إعدادات لا ثوابت. **حدّ الدخول** (عُدّل 2026-10-17، كان 10 محاولات/15 د): بالاتجاه المسجّل في OPEN_ITEMS 16، وأرقامه تُحسم عند الإطلاق (§5.2).
3. **`core.billing.read`:** owner وadmin.
4. **الـ operator** يُنشئ ويعدّل العملاء (`clients.write`)؛ التعطيل `clients.manage` لـ owner/admin.
5. **outbox** بدل الإرسال داخل المعاملة (§5.5، §3، §4.1 محدّثة).
6. **`provider_accounts` للمنصة** يزرعه migrator مع الكتالوجات.

### ما يلي اعتماد الملحق

- PR وثائق: `docs/PLATFORM_CORE_AMENDMENT_01.md` + `_EN.md`، سطر السلطة في `CLAUDE.md`، بنود `OPEN_ITEMS.md` بأرقامها في §1.
- ثم PRs الكود بترتيب الخارطة، كلٌّ على فرعه، وكل ترحيل يعيد توليد `docs/schema.sql` ويحدّث `DATABASE.md`.
