# Glossary

Every text of the interface uses these terms — the English `defaultMessage` in the code and the Arabic in
`locales/ar.json`. Code identifiers (message ids, API fields, error codes) keep the platform's own names
(`tenant`, `tenant_id`, `no_active_tenant`): they are never shown.

| Concept | العربية | English |
|---|---|---|
| tenant | الجهة | Organization |
| membership | العضوية | Membership |
| a member | العضو | Member |
| a disabled member | معطَّل | Disabled |
| a member who has left | غادر | Left |
| withdrawing an invitation | سحب الدعوة | Withdraw |
| role | الدور | Role |
| permission | الصلاحية | Permission |
| scope | النطاق | Scope |
| scope `all` | كل العملاء | All clients |
| scope `assigned` | عملاء محددون | Assigned clients |
| assignment | الإسناد | Assignment |
| invitation | الدعوة | Invitation |
| accepting an invitation | قبول الدعوة | Accept the invitation |
| account | الحساب | Account |
| client | العميل | Client |
| subscription | الاشتراك | Subscription |
| a disabled client | معطَّل | Disabled |
| renewing a subscription | تجديد | Renew |
| cancelling a subscription | إلغاء الاشتراك | Cancel subscription |
| a cancelled subscription | ملغى | Cancelled |
| a renewed subscription | مجدَّد | Renewed |
| the list's filter: active subscriptions | النشطة | Active |
| the list's filter: renewed subscriptions | المجدَّدة | Renewed |
| the list's filter: cancelled subscriptions | الملغاة | Cancelled |
| the list's filter: every status | الكل | All |

The filter names a plural (الاشتراكات), so its Arabic is the feminine of the row's own word: a row is «ملغى», the
filter that shows such rows is «الملغاة».

The original T8 screen rendered by Api (`src/Modules.Subscriptions/SubscriptionScreen.cs`) keeps "المستأجر": it is
part of a merged proof and is not edited (OPEN_ITEMS 29).

## Enforced

`check-i18n-parity` refuses a message that uses the platform's word instead of the glossary's: "tenant" in English,
"مستأجر" in Arabic. Its self-test plants one of each.
