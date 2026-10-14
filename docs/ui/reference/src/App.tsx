import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { ACCENT_STORAGE_KEY, accentIds, applyAccentPreference, isAccentId, readAccentPreference, saveAccentPreference, type AccentId } from "./accentPreference";

type Language = "en" | "ar";
type Theme = "light" | "dark" | "contrast";

const sectionIds = ["sitemap", "foundations", "components", "theme-lab", "screen-inventory", "questions"] as const;
type SectionId = (typeof sectionIds)[number];

const accentPresets: { id: AccentId; en: string; ar: string }[] = [
  { id: "indigo", en: "Indigo", ar: "نيلي" },
  { id: "teal", en: "Teal", ar: "أخضر مزرق" },
  { id: "rust", en: "Rust", ar: "طوبي" },
  { id: "blue", en: "Blue", ar: "أزرق" },
  { id: "violet", en: "Violet", ar: "بنفسجي" },
];

const copy = {
  en: {
    eyebrow: "Platform foundation · Step 01",
    title: "Structure before screens.",
    subtitle:
      "A bilingual navigation model and accessible design system for a modular, multi-tenant SaaS platform.",
    nav: ["Sitemap", "Foundations", "Components", "Theme lab", "Screen inventory", "Decisions"],
    spec: "Specification",
    sitemap: "Sitemap & navigation",
    sitemapIntro:
      "The shell separates platform context, tenant context, and module context. A user's current tenant and module remain visible at every depth.",
    shell: "Global shell",
    foundations: "Design foundations",
    components: "Core components",
    themeLab: "Accessible theme lab",
    openQuestions: "Resolved decisions",
    inventory: "Screen inventory",
    direction: "RTL / LTR",
    density: "Comfortable",
    primary: "Primary",
    preview: "Live preview",
    colorLabel: "Brand color",
    modeLabel: "Display mode",
    contrastPass: "AA pass",
    contrastFail: "Needs adjustment",
    nearest: "Use nearest accessible shade",
    save: "Save appearance",
    cancel: "Cancel",
    search: "Search clients, subscriptions…",
    label: "Client name",
    placeholder: "e.g. Northstar Studio",
    addClient: "Add client",
    accessibility: "Accessibility",
  },
  ar: {
    eyebrow: "أساس المنصة · الخطوة 01",
    title: "الهيكل أولاً، ثم الشاشات.",
    subtitle:
      "نموذج تنقّل ثنائي اللغة ونظام تصميم متاح لمنصة SaaS معيارية ومتعددة المؤسسات.",
    nav: ["خريطة الموقع", "الأسس", "المكوّنات", "مختبر السمة", "مخزون الشاشات", "القرارات"],
    spec: "المواصفات",
    sitemap: "خريطة الموقع والتنقّل",
    sitemapIntro:
      "يفصل إطار التطبيق بين سياق المنصة والمؤسسة والوحدة، ويبقي المؤسسة والوحدة الحاليتين ظاهرتين في كل مستوى.",
    shell: "الإطار العام",
    foundations: "أسس التصميم",
    components: "المكوّنات الأساسية",
    themeLab: "مختبر السمة المتاحة",
    openQuestions: "القرارات المعتمدة",
    inventory: "مخزون الشاشات",
    direction: "من اليمين / اليسار",
    density: "مريح",
    primary: "أساسي",
    preview: "معاينة مباشرة",
    colorLabel: "لون العلامة",
    modeLabel: "نمط العرض",
    contrastPass: "متوافق AA",
    contrastFail: "يحتاج إلى تعديل",
    nearest: "استخدم أقرب درجة متاحة",
    save: "حفظ المظهر",
    cancel: "إلغاء",
    search: "ابحث عن عميل أو اشتراك…",
    label: "اسم العميل",
    placeholder: "مثال: استوديو نورث ستار",
    addClient: "إضافة عميل",
    accessibility: "إمكانية الوصول",
  },
};

const sitemap = [
  {
    key: "identity",
    en: "Identity",
    ar: "الهوية",
    noteEn: "Outside tenant context",
    noteAr: "خارج سياق المؤسسة",
    items: [
      ["Login", "تسجيل الدخول"],
      ["Accept invitation", "قبول الدعوة"],
      ["Choose tenant", "اختيار المؤسسة"],
    ],
  },
  {
    key: "admin",
    en: "Platform Admin",
    ar: "إدارة المنصة",
    noteEn: "Cross-tenant operations",
    noteAr: "عمليات عبر المؤسسات",
    items: [
      ["Tenants", "المؤسسات"],
      ["Create tenant", "إنشاء مؤسسة"],
      ["Suspend tenant", "تعليق مؤسسة"],
      ["Plans", "الخطط"],
    ],
  },
  {
    key: "platform",
    en: "Platform core",
    ar: "نواة المنصة",
    noteEn: "Tenant administration",
    noteAr: "إدارة المؤسسة",
    items: [
      ["Overview", "نظرة عامة"],
      ["Members", "الأعضاء"],
      ["Roles & permissions", "الأدوار والصلاحيات"],
      ["Scope assignments", "نطاقات الوصول"],
      ["Tenant settings", "إعدادات المؤسسة"],
      ["Audit log", "سجل التدقيق"],
    ],
  },
  {
    key: "subscriptions",
    en: "Subscriptions",
    ar: "الاشتراكات",
    noteEn: "First business module",
    noteAr: "أول وحدة أعمال",
    items: [
      ["Dashboard", "لوحة المعلومات"],
      ["Clients", "العملاء"],
      ["Subscriptions", "الاشتراكات"],
      ["Notification log", "سجل الإشعارات"],
      ["Module settings", "إعدادات الوحدة"],
    ],
  },
  {
    key: "personal",
    en: "Personal",
    ar: "شخصي",
    noteEn: "Follows the user",
    noteAr: "يتبع المستخدم",
    items: [
      ["Profile & security", "الملف والأمان"],
      ["Language & appearance", "اللغة والمظهر"],
      ["Notification preferences", "تفضيلات الإشعارات"],
    ],
  },
];

const tokenGroups = [
  {
    en: "Color",
    ar: "الألوان",
    detailEn: "Semantic roles, never fixed usage",
    detailAr: "أدوار دلالية وليست استخدامات ثابتة",
    tokens: [
      ["surface", "Canvas · raised · overlay"],
      ["text", "Primary · secondary · muted · inverse"],
      ["action", "Primary · subtle · disabled"],
      ["status", "Success · warning · danger · info"],
      ["border", "Subtle · strong · focus"],
    ],
  },
  {
    en: "Type",
    ar: "الخط",
    detailEn: "Inter + Noto Sans Arabic",
    detailAr: "Inter + Noto Sans Arabic",
    tokens: [
      ["Display", "40 / 48 · 700"],
      ["Heading 1", "30 / 38 · 700"],
      ["Heading 2", "22 / 30 · 650"],
      ["Body", "15 / 24 · 450"],
      ["Label", "13 / 18 · 600"],
    ],
  },
  {
    en: "Shape & depth",
    ar: "الشكل والعمق",
    detailEn: "Calm, precise, operational",
    detailAr: "هادئ ودقيق وعملي",
    tokens: [
      ["Radius", "6 · 10 · 14 · 20 · full"],
      ["Elevation 1", "Controls & cards"],
      ["Elevation 2", "Menus & sticky bars"],
      ["Elevation 3", "Dialogs only"],
    ],
  },
  {
    en: "Space & density",
    ar: "المسافات والكثافة",
    detailEn: "4pt base grid",
    detailAr: "شبكة أساسية 4 نقاط",
    tokens: [
      ["Spacing", "4 · 8 · 12 · 16 · 24 · 32 · 48 · 64"],
      ["Control", "44 comfortable · 36 compact"],
      ["Table row", "52 comfortable · 40 compact"],
      ["Content", "1280 max · 24 gutter"],
    ],
  },
];

const decisions = [
  [
    "Members have one tenant role plus a client scope. Permissions come from the role; accessible entities come from scope.",
    "يمتلك العضو دوراً واحداً في المؤسسة بالإضافة إلى نطاق عملاء. تأتي الصلاحيات من الدور والكيانات المتاحة من النطاق.",
  ],
  [
    "WhatsApp templates are plain text. Email uses the brand color only for the logo and button, both protected by contrast validation.",
    "قوالب واتساب نصية فقط. يستخدم البريد لون العلامة للشعار والزر فقط، مع حماية كليهما بفحص التباين.",
  ],
  [
    "Expiry is date-only. Send windows use the tenant timezone, with an optional per-client override.",
    "تاريخ الانتهاء دون وقت. تستخدم نوافذ الإرسال منطقة المؤسسة الزمنية مع تجاوز اختياري لكل عميل.",
  ],
  [
    "Templates are human-authored per tenant. Machine translation may appear only as a clearly labeled draft.",
    "تكتب المؤسسة القوالب يدوياً. لا تظهر الترجمة الآلية إلا كمسودة تحمل تسمية واضحة.",
  ],
  [
    "Support mode is deferred. The global shell reserves a persistent, visually distinct banner slot for a future support session.",
    "تم تأجيل وضع الدعم. يحجز الإطار العام موضعاً ثابتاً وواضحاً لشريط جلسة دعم مستقبلي.",
  ],
];

const screenAreas = [
  {
    en: "Identity",
    ar: "الهوية",
    screens: [
      ["Login", "Authenticate and recover access", "Default · invalid credentials · rate-limited · SSO unavailable", "تسجيل الدخول", "المصادقة واستعادة الوصول", "افتراضي · بيانات غير صحيحة · تقييد المحاولات · تعذر SSO"],
      ["Invitation acceptance", "Confirm identity and join a tenant", "Valid · expired · revoked · existing account · new account", "قبول الدعوة", "تأكيد الهوية والانضمام إلى مؤسسة", "صالحة · منتهية · ملغاة · حساب قائم · حساب جديد"],
      ["Tenant selection", "Choose an authorized workspace", "One tenant · many tenants · no active tenant · suspended tenant", "اختيار المؤسسة", "اختيار مساحة عمل مصرح بها", "مؤسسة واحدة · عدة مؤسسات · لا مؤسسة نشطة · مؤسسة معلقة"],
    ],
  },
  {
    en: "Platform Admin",
    ar: "إدارة المنصة",
    screens: [
      ["Tenants", "Find and manage all paying tenants", "Active · trial · past due · suspended · empty · filtered", "المؤسسات", "البحث عن جميع المؤسسات وإدارتها", "نشط · تجريبي · متأخر · معلق · فارغ · مفلتر"],
      ["Create tenant", "Provision tenant, owner, locale, and plan", "Draft · validating · duplicate · provisioning · success", "إنشاء مؤسسة", "تجهيز المؤسسة والمالك واللغة والخطة", "مسودة · تحقق · مكرر · قيد التجهيز · نجاح"],
      ["Tenant administration", "Review plan and suspend or restore access", "Healthy · warning · confirmation · suspended · forbidden", "إدارة المؤسسة", "مراجعة الخطة وتعليق الوصول أو استعادته", "سليم · تحذير · تأكيد · معلق · ممنوع"],
      ["Plans", "Define module access and commercial limits", "Active · archived · unsaved changes · in-use guard", "الخطط", "تحديد الوحدات والحدود التجارية", "نشطة · مؤرشفة · تغييرات غير محفوظة · منع بسبب الاستخدام"],
    ],
  },
  {
    en: "Platform Core",
    ar: "نواة المنصة",
    screens: [
      ["Members", "Manage membership, role, and access status", "Active · invited · suspended · empty · forbidden · bulk selection", "الأعضاء", "إدارة العضوية والدور وحالة الوصول", "نشط · مدعو · معلق · فارغ · ممنوع · تحديد متعدد"],
      ["Roles & permissions", "Define tenant roles and permission bundles", "System role · custom role · conflict · in-use · read-only", "الأدوار والصلاحيات", "تعريف أدوار المؤسسة وحزم الصلاحيات", "دور نظام · مخصص · تعارض · قيد الاستخدام · قراءة فقط"],
      ["Scope assignment", "Assign each member an allowed client subset", "All clients · selected clients · no scope · inherited change", "تعيين النطاق", "تعيين مجموعة العملاء المتاحة لكل عضو", "كل العملاء · عملاء محددون · دون نطاق · تغيير موروث"],
      ["Tenant settings", "Set identity, locale, timezone, and appearance", "Saved · unsaved · invalid brand color · translation draft", "إعدادات المؤسسة", "ضبط الهوية واللغة والمنطقة الزمنية والمظهر", "محفوظ · غير محفوظ · لون غير صالح · مسودة ترجمة"],
      ["Audit log", "Trace authorized security and data events", "Timeline · filtered · exported · empty · restricted", "سجل التدقيق", "تتبع أحداث الأمان والبيانات المصرح بها", "خط زمني · مفلتر · مُصدّر · فارغ · مقيّد"],
    ],
  },
  {
    en: "Subscriptions",
    ar: "الاشتراكات",
    screens: [
      ["Dashboard", "Monitor expiry and delivery health", "Loading · healthy · expiring · expired · failed · scoped", "لوحة المعلومات", "مراقبة الانتهاء وصحة الإرسال", "تحميل · سليم · قريب الانتهاء · منتهي · فشل · محدود بالنطاق"],
      ["Clients", "Manage client entities and contact channels", "List · empty · duplicate · inaccessible · timezone override", "العملاء", "إدارة العملاء وقنوات التواصل", "قائمة · فارغ · مكرر · غير متاح · تجاوز المنطقة الزمنية"],
      ["Subscriptions list", "Find, filter, and act on renewals", "Active · due soon · expired · paused · compact · bulk selection", "قائمة الاشتراكات", "البحث والتصفية وتنفيذ التجديدات", "نشط · قريب · منتهي · متوقف · مضغوط · تحديد متعدد"],
      ["Subscription details", "Review ownership, expiry, and renewal history", "Current · overdue · no history · edited · forbidden action", "تفاصيل الاشتراك", "مراجعة الملكية والانتهاء وسجل التجديد", "حالي · متأخر · دون سجل · معدل · إجراء ممنوع"],
      ["Renew subscription", "Record renewal and next date-only expiry", "Confirm · invalid date · saving · success · conflict", "تجديد الاشتراك", "تسجيل التجديد وتاريخ الانتهاء التالي", "تأكيد · تاريخ غير صالح · حفظ · نجاح · تعارض"],
      ["Client notification preferences", "Set language, tone, channels, windows, and intervals", "Inherited · custom · channel unavailable · draft translation", "تفضيلات إشعارات العميل", "ضبط اللغة والنبرة والقنوات والنوافذ والفواصل", "موروثة · مخصصة · قناة غير متاحة · مسودة ترجمة"],
      ["Notification log", "Inspect pending, sent, and failed deliveries", "Pending · sent · failed · retrying · filtered · no access", "سجل الإشعارات", "فحص الرسائل المعلقة والمرسلة والفاشلة", "معلق · مرسل · فشل · إعادة محاولة · مفلتر · دون وصول"],
    ],
  },
];

function Icon({
  name,
  size = 18,
}: {
  name: "grid" | "layers" | "globe" | "moon" | "user" | "check" | "warn" | "arrow" | "search" | "chevron" | "palette";
  size?: number;
}) {
  const paths: Record<string, ReactNode> = {
    grid: <><rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><rect x="3" y="14" width="7" height="7" rx="1" /><rect x="14" y="14" width="7" height="7" rx="1" /></>,
    layers: <><path d="m12 3-9 5 9 5 9-5-9-5Z" /><path d="m3 12 9 5 9-5" /><path d="m3 16 9 5 9-5" /></>,
    globe: <><circle cx="12" cy="12" r="9" /><path d="M3 12h18M12 3a15 15 0 0 1 0 18M12 3a15 15 0 0 0 0 18" /></>,
    moon: <path d="M20 15.2A8.8 8.8 0 0 1 8.8 4 9 9 0 1 0 20 15.2Z" />,
    user: <><circle cx="12" cy="8" r="4" /><path d="M4 21a8 8 0 0 1 16 0" /></>,
    check: <path d="m5 12 4 4L19 6" />,
    warn: <><path d="M12 3 2 21h20L12 3Z" /><path d="M12 9v5M12 18h.01" /></>,
    arrow: <><path d="M5 12h14" /><path d="m14 7 5 5-5 5" /></>,
    search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></>,
    chevron: <path d="m9 18 6-6-6-6" />,
    palette: <><path d="M12 3a9 9 0 0 0 0 18h1.5a1.5 1.5 0 0 0 0-3H12a2 2 0 0 1 0-4h3.5A5.5 5.5 0 0 0 21 8.5C21 5.5 17 3 12 3Z" /><circle cx="7.5" cy="10.5" r=".8" fill="currentColor" stroke="none" /><circle cx="10" cy="7" r=".8" fill="currentColor" stroke="none" /><circle cx="14" cy="6.5" r=".8" fill="currentColor" stroke="none" /><circle cx="17" cy="9" r=".8" fill="currentColor" stroke="none" /></>,
  };
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {paths[name]}
    </svg>
  );
}

function normalizeHex(hex: string) {
  const value = hex.trim().replace("#", "");
  return value.length === 3 ? value.split("").map((c) => c + c).join("") : value;
}

function contrast(hex1: string, hex2: string) {
  const luminance = (hex: string) => {
    const rgb = normalizeHex(hex).match(/.{2}/g)?.map((c) => parseInt(c, 16) / 255) ?? [0, 0, 0];
    const [r, g, b] = rgb.map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const l1 = luminance(hex1);
  const l2 = luminance(hex2);
  return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
}

function cssColor(name: string) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

function accentColor(id: AccentId, mode: Theme) {
  return cssColor(`--accent-${id}-${mode}`);
}

function accentOnColor(mode: Theme) {
  return cssColor(`--on-accent-${mode}`);
}

const validationModes = {
  light: { canvas: "#F4F5F7", surface: "#FFFFFF", text: "#17181C", secondary: "#5D6069", border: "#767982", navSurface: "#17181C", navText: "#D7D8DE", navActiveBg: "#8B84FF", navActiveText: "#101114" },
  dark: { canvas: "#101114", surface: "#18191D", text: "#F3F3F5", secondary: "#B5B7C0", border: "#777A84", navSurface: "#0B0C0F", navText: "#D7D8DE", navActiveBg: "#8B84FF", navActiveText: "#101114" },
  contrast: { canvas: "#FFFFFF", surface: "#FFFFFF", text: "#000000", secondary: "#1C1C1C", border: "#000000", navSurface: "#000000", navText: "#FFFFFF", navActiveBg: "#FFFFFF", navActiveText: "#000000" },
};

function validationRows(tenantBrand: string, mode: Theme) {
  const base = validationModes[mode];
  const brand = mode === "dark" ? darkVariant(tenantBrand) : tenantBrand;
  const onBrand = mode === "dark" ? "#101114" : "#FFFFFF";
  const p = mode === "contrast" ? base : { ...base, navActiveBg: darkVariant(tenantBrand), navActiveText: "#101114" };
  return [
    { en: "Primary text / canvas", ar: "النص الأساسي / الخلفية", fg: p.text, bg: p.canvas, minimum: 4.5, kind: "text" },
    { en: "Secondary text / surface", ar: "النص الثانوي / السطح", fg: p.secondary, bg: p.surface, minimum: 4.5, kind: "text" },
    { en: "Button text / brand", ar: "نص الزر / لون العلامة", fg: onBrand, bg: brand, minimum: 4.5, kind: "text" },
    { en: "Brand control / canvas", ar: "عنصر العلامة / الخلفية", fg: brand, bg: p.canvas, minimum: 3, kind: "ui" },
    { en: "Input border / surface", ar: "حد الحقل / السطح", fg: p.border, bg: p.surface, minimum: 3, kind: "ui" },
    { en: "Focus ring / surface", ar: "حلقة التركيز / السطح", fg: brand, bg: p.surface, minimum: 3, kind: "focus" },
    { en: "Navigation text / navigation surface", ar: "نص التنقل / سطح التنقل", fg: p.navText, bg: p.navSurface, minimum: 4.5, kind: "text" },
    { en: "Active navigation text / active background", ar: "نص التنقل النشط / الخلفية النشطة", fg: p.navActiveText, bg: p.navActiveBg, minimum: 4.5, kind: "text" },
    { en: "Active navigation indicator / navigation surface", ar: "مؤشر التنقل النشط / سطح التنقل", fg: p.navActiveBg, bg: p.navSurface, minimum: 3, kind: "ui" },
  ].map((row) => ({ ...row, ratio: contrast(row.fg, row.bg), pass: contrast(row.fg, row.bg) >= row.minimum }));
}

function accentValidationRows(mode: Theme) {
  const palette = validationModes[mode];
  return accentPresets.map((preset) => {
    const color = accentColor(preset.id, mode);
    const textRatio = contrast(accentOnColor(mode), color);
    const navColor = mode === "contrast" ? palette.navActiveBg : accentColor(preset.id, "dark");
    const uiRatio = Math.min(contrast(color, palette.canvas), contrast(color, palette.surface), contrast(navColor, palette.navSurface));
    return {
      ...preset,
      color,
      textRatio,
      uiRatio,
      pass: textRatio >= 4.5 && uiRatio >= 3,
    };
  });
}

function mixHex(hex: string, target: "#000000" | "#FFFFFF", amount: number) {
  const source = normalizeHex(hex).match(/.{2}/g)?.map((c) => parseInt(c, 16)) ?? [0, 0, 0];
  const destination = target === "#FFFFFF" ? 255 : 0;
  return `#${source.map((value) => Math.round(value + (destination - value) * amount).toString(16).padStart(2, "0")).join("")}`.toUpperCase();
}

// Lighter variant of a brand color for dark surfaces (dark mode and the dark sidebar).
function darkVariant(brand: string) {
  for (let step = 0; step <= 100; step += 1) {
    const candidate = step === 0 ? `#${normalizeHex(brand)}`.toUpperCase() : mixHex(brand, "#FFFFFF", step / 100);
    if (contrast(candidate, "#18191D") >= 3 && contrast(candidate, "#17181C") >= 3 && contrast(candidate, "#101114") >= 4.5) return candidate;
  }
  return "#8B84FF";
}

function suggestAccessibleBrand(brand: string) {
  const candidates = [brand];
  for (let step = 1; step <= 100; step += 1) {
    candidates.push(mixHex(brand, "#000000", step / 100), mixHex(brand, "#FFFFFF", step / 100));
  }
  return candidates.find((candidate) =>
    (Object.keys(validationModes) as Theme[]).every((mode) =>
      validationRows(candidate, mode).filter((row) => row.kind !== "ui" || row.en.startsWith("Brand")).filter((row) => !row.en.startsWith("Primary") && !row.en.startsWith("Secondary")).every((row) => row.pass),
    ),
  ) ?? "#5146E5";
}

function App() {
  const [lang, setLang] = useState<Language>("en");
  const [theme, setTheme] = useState<Theme>("light");
  const [brand, setBrand] = useState("#5146E5");
  const [density, setDensity] = useState<"comfortable" | "compact">("comfortable");
  const [activeSection, setActiveSection] = useState<SectionId>("sitemap");
  const [accent, setAccent] = useState<AccentId | null>(() => readAccentPreference());
  const [accentOpen, setAccentOpen] = useState(false);
  const accentButtonRef = useRef<HTMLButtonElement>(null);
  const accentPopoverRef = useRef<HTMLDivElement>(null);
  const accentOptionRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const t = copy[lang];
  const isRtl = lang === "ar";
  const matrix = useMemo(() => (Object.keys(validationModes) as Theme[]).map((mode) => ({ mode, rows: validationRows(brand, mode) })), [brand]);
  const relevantRows = matrix.flatMap(({ rows }) => rows);
  const presetMatrix = useMemo(() => (Object.keys(validationModes) as Theme[]).map((mode) => ({ mode, rows: accentValidationRows(mode) })), []);
  const presetValidationRows = presetMatrix.flatMap(({ rows }) => rows);
  const accessible = relevantRows.every((row) => row.pass) && presetValidationRows.every((row) => row.pass);
  const suggested = useMemo(() => suggestAccessibleBrand(brand), [brand]);

  useEffect(() => {
    applyAccentPreference(accent);
  }, [accent]);

  useEffect(() => {
    const syncPreference = (event: StorageEvent) => {
      if (event.key !== ACCENT_STORAGE_KEY) return;
      const nextAccent = isAccentId(event.newValue) ? event.newValue : null;
      applyAccentPreference(nextAccent);
      setAccent(nextAccent);
    };
    window.addEventListener("storage", syncPreference);
    return () => window.removeEventListener("storage", syncPreference);
  }, []);

  useEffect(() => {
    if (!accentOpen) return;

    const selectedIndex = accent ? accentIds.indexOf(accent) : 0;
    accentOptionRefs.current[selectedIndex]?.focus();

    const closeOnOutsidePress = (event: PointerEvent) => {
      if (
        accentPopoverRef.current?.contains(event.target as Node) ||
        accentButtonRef.current?.contains(event.target as Node)
      ) return;
      setAccentOpen(false);
    };
    document.addEventListener("pointerdown", closeOnOutsidePress);
    return () => document.removeEventListener("pointerdown", closeOnOutsidePress);
  }, [accentOpen, accent]);

  const chooseAccent = (nextAccent: AccentId | null) => {
    setAccent(nextAccent);
    saveAccentPreference(nextAccent);
  };

  const closeAccentPicker = () => {
    setAccentOpen(false);
    accentButtonRef.current?.focus();
  };

  const handleAccentKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (event.key === "Escape") {
      event.preventDefault();
      closeAccentPicker();
      return;
    }

    const lastIndex = accentPresets.length - 1;
    let nextIndex: number | null = null;
    if (event.key === "ArrowDown" || event.key === "ArrowRight") nextIndex = index === lastIndex ? 0 : index + 1;
    if (event.key === "ArrowUp" || event.key === "ArrowLeft") nextIndex = index === 0 ? lastIndex : index - 1;
    if (event.key === "Home") nextIndex = 0;
    if (event.key === "End") nextIndex = lastIndex;
    if (nextIndex === null) return;

    event.preventDefault();
    const nextAccent = accentPresets[nextIndex].id;
    chooseAccent(nextAccent);
    accentOptionRefs.current[nextIndex]?.focus();
  };

  useEffect(() => {
    let animationFrame: number | null = null;

    const updateActiveSection = () => {
      const shellOffset =
        (document.querySelector<HTMLElement>(".topbar")?.offsetHeight ?? 0) +
        (document.querySelector<HTMLElement>(".support-slot")?.offsetHeight ?? 0) +
        24;

      let currentSection: SectionId = sectionIds[0];

      if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2) {
        currentSection = sectionIds[sectionIds.length - 1];
      } else {
        for (const id of sectionIds) {
          const section = document.getElementById(id);
          if (!section || section.getBoundingClientRect().top > shellOffset) break;
          currentSection = id;
        }
      }

      setActiveSection((current) => current === currentSection ? current : currentSection);
      animationFrame = null;
    };

    const scheduleUpdate = () => {
      if (animationFrame !== null) return;
      animationFrame = window.requestAnimationFrame(updateActiveSection);
    };

    scheduleUpdate();
    window.addEventListener("scroll", scheduleUpdate, { passive: true });
    window.addEventListener("resize", scheduleUpdate);

    return () => {
      window.removeEventListener("scroll", scheduleUpdate);
      window.removeEventListener("resize", scheduleUpdate);
      if (animationFrame !== null) window.cancelAnimationFrame(animationFrame);
    };
  }, []);

  const scrollTo = (id: SectionId) => {
    setActiveSection(id);
    document.getElementById(id)?.scrollIntoView({ behavior: "smooth" });
  };

  return (
    <div className={`app theme-${theme}`} dir={isRtl ? "rtl" : "ltr"} lang={lang} style={accent ? undefined : ({ "--brand": theme === "dark" ? darkVariant(brand) : brand, "--nav-accent": darkVariant(brand), ...(theme === "dark" ? { "--on-brand": "#101114" } : {}) } as React.CSSProperties)}>
      <a className="skip-link" href="#main">Skip to content</a>
      <header className="topbar">
        <div className="brand-lockup">
          <span className="brand-mark"><Icon name="layers" size={20} /></span>
          <span className="brand-name">{lang === "en" ? "Platform" : "المنصة"}<span>Core</span></span>
          <span className="spec-chip">{t.spec} · 01</span>
        </div>
        <div className="shell-controls" aria-label={t.shell}>
          <button className="context-control"><span className="context-icon">N</span><span className="control-copy"><small>{lang === "en" ? "Tenant" : "المؤسسة"}</small>Northstar Group</span><Icon name="chevron" size={15} /></button>
          <button className="context-control module"><Icon name="grid" /><span className="control-copy"><small>{lang === "en" ? "Module" : "الوحدة"}</small>{lang === "en" ? "Subscriptions" : "الاشتراكات"}</span><Icon name="chevron" size={15} /></button>
          <button className="icon-button" onClick={() => setLang(isRtl ? "en" : "ar")} aria-label={isRtl ? "Switch to English" : "التبديل إلى العربية"} lang={isRtl ? "en" : "ar"}><Icon name="globe" /><span>{isRtl ? "EN" : "ع"}</span></button>
          <button className="icon-button" onClick={() => setTheme(theme === "dark" ? "light" : "dark")} aria-label={lang === "en" ? "Toggle theme" : "تبديل الوضع"}><Icon name="moon" /></button>
          <div className="accent-picker">
            <button
              ref={accentButtonRef}
              className="icon-button"
              aria-label={lang === "en" ? "Accent color" : "لون الواجهة"}
              aria-haspopup="dialog"
              aria-expanded={accentOpen}
              aria-controls="accent-popover"
              onClick={() => setAccentOpen((open) => !open)}
            >
              <Icon name="palette" />
            </button>
            {accentOpen && (
              <div
                id="accent-popover"
                ref={accentPopoverRef}
                className="accent-popover"
                role="dialog"
                aria-label={lang === "en" ? "Accent color" : "لون الواجهة"}
                onKeyDown={(event) => {
                  if (event.key !== "Escape") return;
                  event.preventDefault();
                  closeAccentPicker();
                }}
              >
                <div className="accent-popover-heading">
                  <strong>{lang === "en" ? "Accent color" : "لون الواجهة"}</strong>
                  <small>{lang === "en" ? "Personal preference" : "تفضيل شخصي"}</small>
                </div>
                {theme === "contrast" && (
                  <p className="accent-contrast-note" role="note">
                    <Icon name="warn" size={16} />
                    <span>{lang === "en" ? "High contrast replaces accent colors with black and white. Your choice is saved and returns when you switch modes." : "وضع التباين العالي يستبدل ألوان الواجهة بالأسود والأبيض. يُحفظ اختيارك ويعود عند تغيير الوضع."}</span>
                  </p>
                )}
                <div className="accent-options" role="radiogroup" aria-label={lang === "en" ? "Accent presets" : "ألوان الواجهة الجاهزة"}>
                  {accentPresets.map((preset, index) => (
                    <button
                      ref={(node) => { accentOptionRefs.current[index] = node; }}
                      className="accent-option"
                      role="radio"
                      aria-checked={accent === preset.id}
                      tabIndex={accent === preset.id || (!accent && index === 0) ? 0 : -1}
                      key={preset.id}
                      onClick={() => chooseAccent(preset.id)}
                      onKeyDown={(event) => handleAccentKeyDown(event, index)}
                    >
                      <span className={`accent-swatch accent-${preset.id}${theme === "contrast" ? " inactive" : ""}`} />
                      <span>{lang === "en" ? preset.en : preset.ar}</span>
                      <span className="accent-check">{accent === preset.id && <Icon name="check" size={16} />}</span>
                    </button>
                  ))}
                </div>
                <button className="accent-reset" onClick={() => chooseAccent(null)}>
                  {lang === "en" ? "Reset to organization default" : "إعادة ضبط لون المؤسسة"}
                </button>
              </div>
            )}
          </div>
          <button className="avatar" aria-label={lang === "en" ? "Open user menu" : "فتح قائمة المستخدم"}>SA</button>
        </div>
      </header>
      <div className="support-slot" role="status">
        <span>{lang === "en" ? "Reserved support-session banner" : "موضع محجوز لشريط جلسة الدعم"}</span>
        <small>{lang === "en" ? "Inactive · support mode deferred" : "غير نشط · وضع الدعم مؤجل"}</small>
      </div>

      <aside className="sidebar" aria-label="Specification sections">
        <div className="side-heading">{t.eyebrow}</div>
        <nav>
          {sectionIds.map((id, index) => (
            <button key={id} className={`side-link ${activeSection === id ? "active" : ""}`} aria-current={activeSection === id ? "location" : undefined} onClick={() => scrollTo(id)}>
              <span>0{index + 1}</span>{t.nav[index]}
            </button>
          ))}
        </nav>
        <div className="side-note">
          <span className="live-dot" />
          <div><strong>Scope locked</strong><small>{lang === "en" ? "No detailed screens yet" : "لا شاشات تفصيلية بعد"}</small></div>
        </div>
      </aside>

      <main id="main">
        <section className="hero">
          <div>
            <p className="eyebrow">{t.eyebrow}</p>
            <h1>{t.title}</h1>
            <p className="hero-copy">{t.subtitle}</p>
          </div>
          <div className="hero-meta">
            <div><span>01</span><small>{lang === "en" ? "Navigation model" : "نموذج التنقّل"}</small></div>
            <div><span>02</span><small>{lang === "en" ? "Design language" : "لغة التصميم"}</small></div>
            <div><span>AA</span><small>WCAG 2.2</small></div>
          </div>
        </section>

        <section id="sitemap" className="doc-section">
          <div className="section-heading">
            <div><span className="section-number">01</span><p className="eyebrow">{lang === "en" ? "Information architecture" : "هندسة المعلومات"}</p><h2>{t.sitemap}</h2></div>
            <p>{t.sitemapIntro}</p>
          </div>

          <div className="shell-map">
            <div className="shell-map-label">{t.shell}</div>
            <div className="shell-support-slot"><Icon name="user" size={15} /><span>{lang === "en" ? "Reserved: future support-session identity, reason, timer, and exit action" : "محجوز: هوية جلسة الدعم المستقبلية والسبب والمؤقت وإجراء الخروج"}</span></div>
            <div className="shell-strip">
              <div><Icon name="layers" /><span><small>{lang === "en" ? "Tenant" : "المؤسسة"}</small>Northstar Group</span></div>
              <span className="flow-arrow"><Icon name="arrow" /></span>
              <div><Icon name="grid" /><span><small>{lang === "en" ? "Module" : "الوحدة"}</small>{lang === "en" ? "Subscriptions" : "الاشتراكات"}</span></div>
              <div className="shell-actions"><Icon name="search" /><Icon name="globe" /><Icon name="moon" /><Icon name="user" /></div>
            </div>
            <p><Icon name="check" size={15} /> {lang === "en" ? "Context survives language, theme, and module changes." : "يبقى السياق محفوظاً عند تغيير اللغة والسمة والوحدة."}</p>
          </div>

          <div className="sitemap-grid">
            {sitemap.map((group, i) => (
              <article className="map-card" key={group.key}>
                <div className="map-card-head">
                  <span>0{i + 1}</span>
                  <div><h3>{lang === "en" ? group.en : group.ar}</h3><p>{lang === "en" ? group.noteEn : group.noteAr}</p></div>
                </div>
                <ul>
                  {group.items.map(([en, ar]) => <li key={en}><span>{lang === "en" ? en : ar}</span><Icon name="chevron" size={14} /></li>)}
                </ul>
              </article>
            ))}
          </div>

          <div className="nav-rules">
            {[
              ["Permission-aware", "Hide unavailable modules. Disable contextual actions with a concise reason.", "واعٍ بالصلاحيات", "إخفاء الوحدات غير المتاحة، وتعطيل الإجراءات السياقية مع توضيح السبب."],
              ["Scope-aware", "Lists and counts reflect assigned clients; never imply access beyond scope.", "واعٍ بالنطاق", "تعكس القوائم والأعداد العملاء المعيّنين فقط، دون الإيحاء بوصول أوسع."],
              ["Responsive", "Rail → bottom navigation; tables → cards with priority fields and actions.", "متجاوب", "يتحوّل الشريط الجانبي إلى تنقّل سفلي، والجداول إلى بطاقات مرتبة الأولوية."],
            ].map((item) => (
              <div key={item[0]}><Icon name="check" /><strong>{lang === "en" ? item[0] : item[2]}</strong><p>{lang === "en" ? item[1] : item[3]}</p></div>
            ))}
          </div>
        </section>

        <section id="foundations" className="doc-section">
          <div className="section-heading">
            <div><span className="section-number">02</span><p className="eyebrow">{lang === "en" ? "System language" : "لغة النظام"}</p><h2>{t.foundations}</h2></div>
            <p>{lang === "en" ? "Semantic CSS variables create one coherent interface across tenants, themes, directions, and densities." : "تنشئ متغيرات CSS الدلالية واجهة موحّدة عبر المؤسسات والسمات والاتجاهات والكثافات."}</p>
          </div>
          <div className="token-grid">
            {tokenGroups.map((group, index) => (
              <article className="token-card" key={group.en}>
                <div className="token-title"><span>0{index + 1}</span><div><h3>{lang === "en" ? group.en : group.ar}</h3><p>{lang === "en" ? group.detailEn : group.detailAr}</p></div></div>
                <div className={`token-visual visual-${index}`}>
                  {index === 0 && <><i className="swatch brand" /><i className="swatch success" /><i className="swatch warning" /><i className="swatch danger" /><i className="swatch neutral" /></>}
                  {index === 1 && <><b>Aa</b><b>أب</b><span>01</span></>}
                  {index === 2 && <><i /><i /><i /></>}
                  {index === 3 && <div className="spacing-bars"><i /><i /><i /><i /><i /></div>}
                </div>
                <dl>{group.tokens.map(([name, value]) => <div key={name}><dt>{name}</dt><dd>{value}</dd></div>)}</dl>
              </article>
            ))}
          </div>
          <div className="bidi-callout">
            <div className="bidi-sample"><span>تنتهي رخصة</span> <bdi>example.com</bdi> <span>بتاريخ</span> <bdi>24/08/2025</bdi></div>
            <div><strong>{lang === "en" ? "Bidirectional by design" : "ثنائي الاتجاه منذ البداية"}</strong><p>{lang === "en" ? "Logical properties control layout. Directional icons mirror; domains, dates, URLs, and numbers use isolated bidi runs." : "تتحكم الخصائص المنطقية بالتخطيط. تنعكس الأيقونات الاتجاهية، وتُعزل النطاقات والتواريخ والروابط والأرقام."}</p></div>
          </div>
          <div className="rtl-proof">
            <span className="language-state"><Icon name="globe" />{isRtl ? "العربية · RTL" : "English · LTR"}</span>
            <div><strong>{lang === "en" ? "The complete specification is direction-native" : "المواصفات الكاملة أصلية الاتجاه"}</strong><p>{lang === "en" ? "Use the shell language control to switch the entire shell, foundations, components, theme lab, and inventory to Arabic RTL without losing state." : "استخدم عنصر اللغة في الإطار لتبديل الإطار والأسس والمكوّنات ومختبر السمة والمخزون بالكامل إلى العربية من اليمين إلى اليسار دون فقدان الحالة."}</p></div>
            <button className="btn secondary" onClick={() => setLang(isRtl ? "en" : "ar")}>{isRtl ? "View in English" : "عرض النسخة العربية"}</button>
          </div>
        </section>

        <section id="components" className="doc-section">
          <div className="section-heading">
            <div><span className="section-number">03</span><p className="eyebrow">{lang === "en" ? "React-ready inventory" : "مخزون جاهز لـ React"}</p><h2>{t.components}</h2></div>
            <p>{lang === "en" ? "Every control supports both directions, keyboard operation, 44px touch targets, and visible focus." : "يدعم كل عنصر الاتجاهين ولوحة المفاتيح وأهداف لمس 44 نقطة والتركيز المرئي."}</p>
          </div>
          <div className="component-board">
            <article className="component-panel actions-panel">
              <div className="panel-label">01 · {lang === "en" ? "Actions & feedback" : "الإجراءات والتغذية الراجعة"}</div>
              <div className="button-row">
                <button className="btn primary"><span>+</span>{t.addClient}</button>
                <button className="btn secondary">{lang === "en" ? "Export" : "تصدير"}</button>
                <button className="btn ghost">{t.cancel}</button>
                <button className="btn secondary" disabled>{lang === "en" ? "No permission" : "لا توجد صلاحية"}</button>
              </div>
              <div className="status-row">
                <span className="badge success"><Icon name="check" size={14} />{lang === "en" ? "Sent" : "تم الإرسال"}</span>
                <span className="badge warning"><Icon name="warn" size={14} />{lang === "en" ? "Due soon" : "قريب الاستحقاق"}</span>
                <span className="badge danger"><Icon name="warn" size={14} />{lang === "en" ? "Failed" : "فشل"}</span>
                <span className="badge neutral">3 {lang === "en" ? "drafts" : "مسودات"}</span>
              </div>
              <div className="toast"><span className="toast-icon"><Icon name="check" /></span><div><strong>{lang === "en" ? "Subscription renewed" : "تم تجديد الاشتراك"}</strong><p>{lang === "en" ? "The next reminder is scheduled for 12 Sep." : "تمت جدولة التذكير التالي في 12 سبتمبر."}</p></div><button aria-label="Dismiss">×</button></div>
              <p className="a11y-note"><Icon name="check" size={14} /><strong>{t.accessibility}:</strong> {lang === "en" ? "Status always pairs icon + text. Toasts use polite live regions and never steal focus." : "تقترن الحالة دائماً بأيقونة ونص. تستخدم التنبيهات منطقة حية مهذبة ولا تنقل التركيز."}</p>
            </article>

            <article className="component-panel form-panel">
              <div className="panel-label">02 · {lang === "en" ? "Inputs & selection" : "الإدخال والاختيار"}</div>
              <label className="field"><span>{t.label}<em>*</em></span><input placeholder={t.placeholder} /><small>{lang === "en" ? "Visible to members within this client's scope." : "مرئي للأعضاء ضمن نطاق هذا العميل."}</small></label>
              <label className="field"><span>{lang === "en" ? "Reminder channel" : "قناة التذكير"}</span><button className="selectlike"><span><i className="channel-dot" />WhatsApp + Email</span><Icon name="chevron" size={15} /></button></label>
              <div className="segmented" aria-label="Density">
                <button className={density === "comfortable" ? "active" : ""} onClick={() => setDensity("comfortable")}>{lang === "en" ? "Comfortable" : "مريح"}</button>
                <button className={density === "compact" ? "active" : ""} onClick={() => setDensity("compact")}>{lang === "en" ? "Compact" : "مضغوط"}</button>
              </div>
              <p className="a11y-note"><Icon name="check" size={14} /><strong>{t.accessibility}:</strong> {lang === "en" ? "Persistent labels, linked help and error text, programmatic required state." : "تسميات ثابتة، ونصوص مساعدة وأخطاء مرتبطة، وحالة إلزامية برمجية."}</p>
            </article>

            <article className="component-panel inventory-panel">
              <div className="panel-label">03 · {lang === "en" ? "Complete core inventory" : "المخزون الأساسي الكامل"}</div>
              <div className="inventory-list">
                {[
                  ["Navigation", "Sidebar · tabs · breadcrumbs · pagination", "التنقّل", "شريط جانبي · تبويبات · مسار · ترقيم"],
                  ["Data", "Table · filters · sort · density · bulk actions", "البيانات", "جدول · فلاتر · ترتيب · كثافة · إجراءات مجمّعة"],
                  ["Overlays", "Modal · drawer · popover · tooltip", "الطبقات", "نافذة · درج · نافذة منبثقة · تلميح"],
                  ["States", "Empty · error · forbidden · loading · skeleton", "الحالات", "فارغة · خطأ · ممنوعة · تحميل · هيكل"],
                  ["Workflow", "Date picker · stepper · file upload · confirmation", "سير العمل", "منتقي تاريخ · خطوات · رفع ملف · تأكيد"],
                ].map((row) => <div key={row[0]}><strong>{lang === "en" ? row[0] : row[2]}</strong><span>{lang === "en" ? row[1] : row[3]}</span><Icon name="check" size={16} /></div>)}
              </div>
              <p className="a11y-note"><Icon name="check" size={14} /><strong>{t.accessibility}:</strong> {lang === "en" ? "ARIA-native patterns, trapped modal focus, announced async states, no hover-only content." : "أنماط ARIA أصلية، وحصر التركيز في النوافذ، وإعلان الحالات غير المتزامنة، ولا معلومات عند التحويم فقط."}</p>
            </article>
          </div>
        </section>

        <section id="theme-lab" className="doc-section">
          <div className="section-heading">
            <div><span className="section-number">04</span><p className="eyebrow">{lang === "en" ? "Tenant expression, safely" : "هوية المؤسسة بأمان"}</p><h2>{t.themeLab}</h2></div>
            <p>{lang === "en" ? "Every text, UI, and focus pair is checked in all modes. Suggested corrections are previewed and never applied without consent." : "يُفحص كل زوج للنص والواجهة والتركيز في جميع الأنماط. تُعاين التصحيحات المقترحة ولا تُطبّق دون موافقة."}</p>
          </div>
          <div className="theme-studio">
            <div className="theme-controls">
              <div className="panel-label">{lang === "en" ? "Appearance settings" : "إعدادات المظهر"}</div>
              <fieldset>
                <legend>{t.colorLabel}</legend>
                <div className="palette">
                  {accentPresets.map((preset) => {
                    const color = accentColor(preset.id, "light");
                    return <button key={preset.id} aria-label={`${lang === "en" ? "Choose" : "اختيار"} ${lang === "en" ? preset.en : preset.ar}`} className={`accent-${preset.id} ${brand === color ? "selected" : ""}`} onClick={() => setBrand(color)}>{brand === color && <Icon name="check" size={15} />}</button>;
                  })}
                </div>
                <label className="hex-field"><span>{lang === "en" ? "Custom value" : "قيمة مخصصة"}</span><div><input type="color" value={brand} onChange={(e) => setBrand(e.target.value.toUpperCase())} /><input value={brand} onChange={(e) => /^#[0-9A-Fa-f]{0,6}$/.test(e.target.value) && setBrand(e.target.value.toUpperCase())} aria-label="Hex color" /></div></label>
              </fieldset>
              <fieldset>
                <legend>{t.modeLabel}</legend>
                <div className="mode-cards">
                  {(["light", "dark", "contrast"] as Theme[]).map((mode) => <button key={mode} className={theme === mode ? "selected" : ""} onClick={() => setTheme(mode)}><span className={`mode-preview ${mode}`}><i /><i /><i /></span><span>{mode === "light" ? (lang === "en" ? "Light" : "فاتح") : mode === "dark" ? (lang === "en" ? "Dark" : "داكن") : (lang === "en" ? "High contrast" : "تباين عالٍ")}</span></button>)}
                </div>
              </fieldset>
              <div className={`contrast-result ${accessible ? "pass" : "fail"}`}>
                <span><Icon name={accessible ? "check" : "warn"} /></span>
                <div><strong>{accessible ? t.contrastPass : t.contrastFail}</strong><p>{relevantRows.filter((row) => row.pass).length + presetValidationRows.filter((row) => row.pass).length}/{relevantRows.length + presetValidationRows.length} {lang === "en" ? "token pairs pass" : "من أزواج الرموز ناجحة"}</p></div>
              </div>
              {!accessible && (
                <div className="shade-suggestion">
                  <div><span><i style={{ backgroundColor: brand }} />{lang === "en" ? "Before" : "قبل"} <bdi>{brand}</bdi></span><Icon name="arrow" /><span><i style={{ backgroundColor: suggested }} />{lang === "en" ? "Suggested" : "مقترح"} <bdi>{suggested}</bdi></span></div>
                  <button className="suggestion" onClick={() => setBrand(suggested)}>{lang === "en" ? "Accept suggested shade" : "قبول الدرجة المقترحة"}<Icon name="check" /></button>
                  <small>{lang === "en" ? "Your original value remains active until accepted." : "تبقى قيمتك الأصلية نشطة حتى تقبل الاقتراح."}</small>
                </div>
              )}
            </div>
            <div className="theme-preview">
              <div className="preview-label">{t.preview}<span>{lang === "en" ? "Token-generated" : "مولّد من الرموز"}</span></div>
              <div className="mini-window">
                <aside><span className="mini-logo" /><i /><i className="active" /><i /><i /></aside>
                <div className="mini-main">
                  <div className="mini-header"><div><small>{lang === "en" ? "Subscriptions" : "الاشتراكات"}</small><strong>{lang === "en" ? "Good morning, Sara" : "صباح الخير، سارة"}</strong></div><span>SA</span></div>
                  <div className="mini-search"><Icon name="search" size={14} />{t.search}</div>
                  <div className="mini-stats"><div><small>{lang === "en" ? "Due soon" : "قريباً"}</small><b>24</b></div><div><small>{lang === "en" ? "Renewed" : "تم التجديد"}</small><b>86</b></div></div>
                  <div className="mini-bars" aria-label={lang === "en" ? "Renewals chart" : "مخطط التجديدات"}><i /><i /><i /><i /><i /></div>
                  <button className="btn primary"><span>+</span>{t.addClient}</button>
                </div>
              </div>
              <div className="token-output">
                <span><i style={{ backgroundColor: brand }} />brand/input</span>
                <span><i className="generated" />action/primary</span>
                <span><i className="soft" />action/subtle</span>
                <span><i className="ring" />focus/ring</span>
              </div>
            </div>
          </div>
          <div className="accent-showcase">
            <div className="accent-showcase-heading">
              <div><strong>{lang === "en" ? "Accent picker states" : "حالات منتقي لون الواجهة"}</strong><p>{lang === "en" ? "The global header remains closed by default. These review states show both directions and modes." : "يبقى العنصر في الرأس مغلقاً افتراضياً. تعرض حالات المراجعة الاتجاهين والنمطين."}</p></div>
              <span><Icon name="check" size={14} />{lang === "en" ? "5 pre-validated presets" : "5 ألوان متحققة مسبقاً"}</span>
            </div>
            <div className="accent-state-grid">
              {([
                { language: "en" as Language, direction: "ltr", mode: "light", selected: "indigo" as AccentId },
                { language: "ar" as Language, direction: "rtl", mode: "dark", selected: "violet" as AccentId },
              ]).map((state) => (
                <article className={`accent-state accent-state-${state.mode}`} dir={state.direction} lang={state.language} key={state.language}>
                  <div className="accent-state-header">
                    <div><small>{state.language === "en" ? "Global shell · Light" : "الإطار العام · داكن"}</small><strong>{state.language === "en" ? "Accent color" : "لون الواجهة"}</strong></div>
                    <span className="icon-button"><Icon name="palette" /></span>
                  </div>
                  <div className="accent-popover accent-popover-static">
                    <div className="accent-popover-heading"><strong>{state.language === "en" ? "Accent color" : "لون الواجهة"}</strong><small>{state.language === "en" ? "Personal preference" : "تفضيل شخصي"}</small></div>
                    <div className="accent-options" role="radiogroup" aria-label={state.language === "en" ? "Accent presets example" : "مثال ألوان الواجهة الجاهزة"}>
                      {accentPresets.map((preset) => (
                        <div className="accent-option" role="radio" aria-checked={state.selected === preset.id} key={preset.id}>
                          <span className={`accent-swatch accent-${preset.id}${theme === "contrast" ? " inactive" : ""}`} />
                          <span>{state.language === "en" ? preset.en : preset.ar}</span>
                          <span className="accent-check">{state.selected === preset.id && <Icon name="check" size={16} />}</span>
                        </div>
                      ))}
                    </div>
                    <div className="accent-reset">{state.language === "en" ? "Reset to organization default" : "إعادة ضبط لون المؤسسة"}</div>
                  </div>
                </article>
              ))}
            </div>
            <div className="accent-dashboard-grid">
              {(["indigo", "teal"] as AccentId[]).map((presetId) => {
                const preset = accentPresets.find((item) => item.id === presetId)!;
                return (
                  <article className={`accent-dashboard accent-demo-${presetId}`} key={presetId}>
                    <div className="accent-dashboard-head"><div><small>{lang === "en" ? "Subscriptions dashboard" : "لوحة الاشتراكات"}</small><strong>{lang === "en" ? `${preset.en} accent` : `لون ${preset.ar}`}</strong></div><span>SA</span></div>
                    <div className="accent-dashboard-metrics"><div><small>{lang === "en" ? "Due soon" : "قريباً"}</small><b>24</b></div><div><small>{lang === "en" ? "Renewed" : "تم التجديد"}</small><b>86</b></div><button>{lang === "en" ? "Add subscription" : "إضافة اشتراك"}</button></div>
                    <div className="accent-bars" aria-label={lang === "en" ? `${preset.en} dashboard chart example` : `مثال مخطط بلون ${preset.ar}`}><i /><i /><i /><i /><i /><i /></div>
                  </article>
                );
              })}
            </div>
          </div>
          <div className="contrast-matrix">
            <div className="matrix-heading"><div><strong>{lang === "en" ? "WCAG token-pair matrix" : "مصفوفة أزواج رموز WCAG"}</strong><p>{lang === "en" ? "Text requires 4.5:1; UI boundaries and focus indicators require 3:1." : "يتطلب النص 4.5:1، وتتطلب حدود الواجهة ومؤشرات التركيز 3:1."}</p></div><span className={accessible ? "matrix-pass" : "matrix-fail"}><Icon name={accessible ? "check" : "warn"} size={14} />{accessible ? t.contrastPass : t.contrastFail}</span></div>
            <div className="matrix-grid">
              {matrix.map(({ mode, rows }) => (
                <article key={mode}>
                  <div className="matrix-mode"><span className={`mode-preview ${mode}`}><i /><i /><i /></span><strong>{mode === "light" ? (lang === "en" ? "Light" : "فاتح") : mode === "dark" ? (lang === "en" ? "Dark" : "داكن") : (lang === "en" ? "High contrast" : "تباين عالٍ")}</strong></div>
                  {rows.map((row) => (
                    <div className="matrix-row" key={row.en}>
                      <span className="pair-swatches"><i style={{ backgroundColor: row.fg }} /><i style={{ backgroundColor: row.bg }} /></span>
                      <span><strong>{lang === "en" ? row.en : row.ar}</strong><small>{row.kind} · {row.minimum}:1 min</small></span>
                      <b>{row.ratio.toFixed(2)}</b>
                      <span className={row.pass ? "pass-mark" : "fail-mark"}><Icon name={row.pass ? "check" : "warn"} size={13} />{row.pass ? (lang === "en" ? "Pass" : "ناجح") : (lang === "en" ? "Fail" : "فشل")}</span>
                    </div>
                  ))}
                </article>
              ))}
            </div>
            <div className="preset-matrix">
              <div className="preset-matrix-heading"><strong>{lang === "en" ? "Pre-validated accent presets" : "ألوان الواجهة المتحققة مسبقاً"}</strong><small>{lang === "en" ? "Text / UI & focus ratios" : "نسب النص / الواجهة والتركيز"}</small></div>
              <div className="preset-matrix-table">
                <div className="preset-matrix-row preset-matrix-labels"><span>{lang === "en" ? "Preset" : "اللون"}</span><span>{lang === "en" ? "Light" : "فاتح"}</span><span>{lang === "en" ? "Dark" : "داكن"}</span><span>{lang === "en" ? "High contrast" : "تباين عالٍ"}</span></div>
                {accentPresets.map((preset) => (
                  <div className="preset-matrix-row" key={preset.id}>
                    <span><i className={`accent-swatch accent-${preset.id}`} /><strong>{lang === "en" ? preset.en : preset.ar}</strong></span>
                    {presetMatrix.map(({ mode, rows }) => {
                      const row = rows.find((item) => item.id === preset.id)!;
                      return <span className={row.pass ? "pass-mark" : "fail-mark"} key={mode}><Icon name={row.pass ? "check" : "warn"} size={13} /><b>{row.textRatio.toFixed(2)}</b><small>/ {row.uiRatio.toFixed(2)}</small></span>;
                    })}
                  </div>
                ))}
              </div>
            </div>
          </div>
        </section>

        <section id="screen-inventory" className="doc-section">
          <div className="section-heading">
            <div><span className="section-number">05</span><p className="eyebrow">{lang === "en" ? "Step 03 · no hi-fi" : "الخطوة 03 · دون تصميم نهائي"}</p><h2>{t.inventory}</h2></div>
            <p>{lang === "en" ? "A purpose-and-state contract for every required area. These are planning artifacts, not detailed layouts." : "عقد يحدد الغرض والحالات لكل منطقة مطلوبة. هذه أدوات تخطيط وليست تخطيطات تفصيلية."}</p>
          </div>
          <div className="screen-areas">
            {screenAreas.map((area, areaIndex) => (
              <article className="screen-area" key={area.en}>
                <div className="screen-area-heading"><span>0{areaIndex + 1}</span><h3>{lang === "en" ? area.en : area.ar}</h3><small>{area.screens.length} {lang === "en" ? "screens" : "شاشات"}</small></div>
                <div className="screen-list">
                  {area.screens.map((screen, index) => (
                    <div className="screen-row" key={screen[0]}>
                      <span className="screen-index">{String(index + 1).padStart(2, "0")}</span>
                      <div><strong>{lang === "en" ? screen[0] : screen[3]}</strong><p>{lang === "en" ? screen[1] : screen[4]}</p></div>
                      <div className="state-copy"><small>{lang === "en" ? "Key states" : "الحالات الأساسية"}</small><p>{lang === "en" ? screen[2] : screen[5]}</p></div>
                    </div>
                  ))}
                </div>
              </article>
            ))}
          </div>
          <div className="inventory-guardrails">
            <span><Icon name="check" />{lang === "en" ? "Every list includes scoped, empty, loading, and error behavior." : "تتضمن كل قائمة سلوك النطاق والفراغ والتحميل والخطأ."}</span>
            <span><Icon name="check" />{lang === "en" ? "Forbidden actions explain the missing permission." : "توضح الإجراءات الممنوعة الصلاحية الناقصة."}</span>
            <span><Icon name="check" />{lang === "en" ? "Dashboard and list inventory includes tablet and PWA adaptation." : "يشمل مخزون اللوحات والقوائم التكيف للجهاز اللوحي وتطبيق PWA."}</span>
          </div>
        </section>

        <section id="questions" className="doc-section questions-section">
          <div className="section-heading">
            <div><span className="section-number">06</span><p className="eyebrow">{lang === "en" ? "Product constraints" : "قيود المنتج"}</p><h2>{t.openQuestions}</h2></div>
            <p>{lang === "en" ? "Approved constraints now carried into the information architecture and screen inventory." : "قيود معتمدة أُدرجت الآن في هندسة المعلومات ومخزون الشاشات."}</p>
          </div>
          <ol className="question-list">
            {decisions.map(([en, ar], index) => <li key={en}><span><Icon name="check" size={14} /></span><p>{lang === "en" ? en : ar}</p></li>)}
          </ol>
          <div className="approval-bar"><div><span className="live-dot" /><p><strong>{lang === "en" ? "Inventory ready for review" : "المخزون جاهز للمراجعة"}</strong><small>{lang === "en" ? "No hi-fi screens have been introduced." : "لم تتم إضافة شاشات عالية الدقة."}</small></p></div><button className="btn primary">{lang === "en" ? "Approve inventory" : "اعتماد المخزون"}<Icon name="arrow" /></button></div>
        </section>
      </main>
    </div>
  );
}

export default App;
