# Glossary

Every text of the interface uses these terms — the English `defaultMessage` in the code and the Arabic in
`locales/ar.json`. Code identifiers (message ids, API fields, error codes) keep the platform's own names
(`tenant`, `tenant_id`, `no_active_tenant`): they are never shown.

| Concept | العربية | English |
|---|---|---|
| tenant | الجهة | Organization |
| membership | العضوية | Membership |
| role | الدور | Role |
| permission | الصلاحية | Permission |
| scope | النطاق | Scope |
| scope `all` | كل العملاء | All clients |
| scope `assigned` | عملاء محددون | Assigned clients |
| assignment | الإسناد | Assignment |
| invitation | الدعوة | Invitation |

The original T8 screen rendered by Api (`src/Modules.Subscriptions/SubscriptionScreen.cs`) keeps "المستأجر": it is
part of a merged proof and is not edited (OPEN_ITEMS 29).

## Enforced

`check-i18n-parity` refuses a message that uses the platform's word instead of the glossary's: "tenant" in English,
"مستأجر" in Arabic. Its self-test plants one of each.
