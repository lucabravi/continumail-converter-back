# ContinuMail Converter — Conversion Fidelity

_What gets converted from Thunderbird into an Outlook PST, field by field — and what doesn't, with the reason why._

**Status column:** `Converted` = full fidelity · `Partial` = comes across but approximated/optional (see Notes) · `Planned` = not yet, a deferred follow-up · `Not conv.` = not converted, Notes says why.

> **Mail flag/tag fidelity needs a Thunderbird _profile_.** Pointed at a full profile, the converter reads each folder's `.msf` index for authoritative read/unread, replied/forwarded, starred, junk and tag state. From a bare **mbox** file, those are recovered only where inline `X-Mozilla-Status` headers exist. Calendar, Contacts and Tasks always come from the profile's own stores.

<!--
  EDITING THIS FILE (hand-maintained — no generator is committed):
  * The tables are fenced box-drawing grids; every character INSIDE a box must be single-width.
    No emoji, CJK or other double-width glyphs in any cell — they wobble the borders.
  * The Status column is text-only: Converted / Partial / Planned / Not conv.
  * To edit: pad each cell to its column width, and redraw the ├─┼─┤ separators to match if a
    column widens. Columns align by character count. Keep it user-facing — no PR numbers,
    issue IDs or branch names anywhere in the doc.
-->

---

## ✉️ Mail

_Thunderbird messages → PST mail items_

**Status:** Full fidelity. `.msf` enrichment supplies authoritative read/flag/tag state when a profile is used.

```
┌─────────────────────────┬───────────────────────────┬───────────┬─────────────────────────────┬───────────────────────────────────────┐
│ Source field            │ Goes to                   │ Status    │ Notes / Why                 │ MAPI property                         │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Subject                 │ Subject                   │ Converted │                             │ PidTagSubject                         │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ From / To / Cc / Bcc    │ Sender + recipients       │ Converted │                             │ recipients / PidTagSenderEmailAddress │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Date sent / received    │ Sent & received time      │ Converted │ Date header; MBOX From_ fallback       │ PidTagClientSubmitTime                │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ HTML body               │ HTML body                 │ Converted │                             │ PidTagHtml                            │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Plain-text body         │ Plain-text body           │ Converted │ Derived from HTML if absent │ PidTagBody                            │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Attachments             │ Attachments               │ Converted │ Filename guessed if missing │ (attachment table)                    │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Inline (CID) images     │ Hidden inline attachments │ Converted │ No phantom paperclip        │ PidTagAttachmentHidden                │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Message-ID / References │ Threading + conversation  │ Converted │                             │ PidTagInternetMessageId               │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Read / unread           │ Read state                │ Converted │ From .msf or headers        │ PidTagMessageFlags                    │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Replied / forwarded     │ Reply / forward icons     │ Converted │                             │ PidTagLastVerbExecuted                │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Flagged / starred       │ Star category (yellow)    │ Converted │ Not a To-Do follow-up flag  │ PidNameKeywords (Star)                │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Importance / priority   │ Importance                │ Converted │                             │ PidTagImportance                      │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Tags / labels           │ Categories                │ Converted │ Names from prefs.js         │ PidNameKeywords                       │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Junk status             │ Junk category / folder    │ Converted │ Configurable                │ PidNameKeywords / folder              │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Expunged messages       │ (optionally dropped)      │ Partial   │ Dropped if configured       │ —                                     │
├─────────────────────────┼───────────────────────────┼───────────┼─────────────────────────────┼───────────────────────────────────────┤
│ Folder structure        │ PST folder tree           │ Converted │ mirror or flatten           │ (folder hierarchy)                    │
└─────────────────────────┴───────────────────────────┴───────────┴─────────────────────────────┴───────────────────────────────────────┘
```

The mail mapping keeps the original converter semantics while preserving the fields that have a
direct PST representation. A valid MIME `Sender` is written to the MAPI `Sender*` properties and
`From` remains the `SentRepresenting*` identity. A valid `Reply-To` list is written as the paired
MAPI reply-recipient name and `FLATENTRYLIST` properties, preserving all usable addresses and
their order. `To`, `Cc`, and `Bcc` recipient tables likewise retain every mailbox. If those visible
recipient lists contain no usable mailbox, valid `Delivered-To` envelope addresses are used as a
fallback for `To`; the recipient warning is emitted only when that fallback is also unavailable or
unparseable. For MBOX messages, a valid `Date` header remains authoritative. If it is absent or
malformed, the MBOX `From_` envelope timestamp is used silently when it can be parsed; otherwise a
valid `Received`/`X-Received` timestamp is used with `[integrity:date-received-fallback]`. If no
fallback is available, `[integrity:date-missing]` is emitted and the writer default remains in effect.
`From` is
single-valued in the PST sender properties, so a MIME `From` with multiple mailboxes writes the
first one and emits `[integrity:from-multiple]`. Integrity warnings are emitted only when a
non-empty source value is missing, altered, or not representable in the PST; genuinely absent or
empty source fields are left silent. A bare valid `From: <address>` is safe: the PST sender display
name falls back to the address so Outlook can render the sender line. Gmail-style unnamed CID
images referenced by the HTML are classified as hidden inline resources. Microsoft TNEF parts are
expanded when possible while the original `winmail.dat` bytes remain preserved. Subjects exceeding
the writer's 253-character PST compatibility limit emit `[integrity:subject-truncated]`; source
control characters emit `[integrity:subject-control-characters]`. The report groups each warning
code separately and includes a count plus examples.

---

## 📅 Calendar

_Thunderbird events → PST appointments_

**Status:** Full fidelity for core fields, recurrence + exceptions, time zones, attendees and attachments. Online-meeting join links and item relations are preserved in the body (no native Outlook slot exists for them).

```
┌─────────────────────────────┬───────────────────────────────┬───────────┬────────────────────────────────────┬───────────────────────────────────┐
│ Source field                │ Goes to                       │ Status    │ Notes / Why                        │ MAPI property                     │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Title                       │ Appointment subject           │ Converted │                                    │ PidTagSubject                     │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Start / end + time zone     │ Start & end, with time zone   │ Converted │ Time zone preserved                │ AppointmentStartWhole/EndWhole    │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ All-day flag                │ Shown as all-day event        │ Converted │ Local-midnight start/end           │ AppointmentSubType                │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Priority                    │ Importance                    │ Converted │                                    │ PidTagImportance                  │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Privacy / class             │ Sensitivity + private flag    │ Converted │                                    │ PidTagSensitivity + PidLidPrivate │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Free / busy status          │ Show-as free/busy/tentative   │ Converted │                                    │ PidLidBusyStatus                  │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Description                 │ Body (plain + HTML)           │ Partial   │ Plain text derived from HTML       │ PidTagBody (+PidTagHtml)          │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Location                    │ Location                      │ Converted │ Plain text                         │ PidLidLocation                    │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Categories                  │ Categories                    │ Converted │                                    │ PidNameKeywords                   │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Reminders (alarms)          │ Reminder + lead time          │ Partial   │ After-start alarm dropped + warn   │ PidLidReminderSet + Delta         │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Attendees + responses       │ Meeting recipients + tracking │ Converted │ ROLE + PARTSTAT preserved          │ recipients / ResponseStatus       │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Recurrence (RRULE) + exc.   │ Recurring series + exceptions │ Converted │ EXDATE + modified occurrences      │ PidLidAppointmentRecur            │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Event attachments           │ PST attachments               │ Converted │ Inline embedded; links in body     │ (attachment table)                │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Teams / Google Meet link    │ Join link in the body         │ Partial   │ Classic Outlook has no Join button │ PidTagBody (+PidTagHtml)          │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Meeting identity (GOID)     │ Meeting global object id      │ Converted │ Exchange/M365-cached events only   │ PidLidGlobalObjectId              │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ Item relations (RELATED-TO) │ Preserved note in body        │ Partial   │ No native Outlook relation slot    │ PidTagBody                        │
├─────────────────────────────┼───────────────────────────────┼───────────┼────────────────────────────────────┼───────────────────────────────────┤
│ CalDAV sync ETag / href     │ (nothing - dropped)           │ Not conv. │ Server-only; no PST slot           │ —                                 │
└─────────────────────────────┴───────────────────────────────┴───────────┴────────────────────────────────────┴───────────────────────────────────┘
```

---

## 👥 Contacts

_Thunderbird address book → PST contacts_

**Status:** Shipped. Distribution lists / groups are a deferred follow-up.

```
┌─────────────────────────────┬────────────────────────┬───────────┬────────────────────┬─────────────────────────────────────┐
│ Source field                │ Goes to                │ Status    │ Notes / Why        │ MAPI property                       │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Display name                │ Full name              │ Converted │                    │ PidTagDisplayName                   │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ First / middle / last       │ Name parts             │ Converted │                    │ PidTagGivenName / Surname           │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Email addresses             │ Email 1 / 2 / 3        │ Converted │                    │ PidLidEmail1EmailAddress            │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Phone numbers               │ Phone fields           │ Converted │ Mapped by type     │ PidTagBusinessTelephoneNumber       │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Mailing address             │ Address fields         │ Converted │                    │ PidLidWorkAddress*                  │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Organization / title        │ Company + job title    │ Converted │                    │ PidTagCompanyName / Title           │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Photo                       │ Contact photo          │ Converted │                    │ PidLidHasPicture + attachment       │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Notes                       │ Notes body             │ Converted │                    │ PidTagBody                          │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Birthday / anniversary      │ Date fields            │ Converted │                    │ PidTagBirthday / WeddingAnniversary │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Website / IM                │ URL / IM fields        │ Converted │                    │ PidLidHtml / PidLidInstantMessaging │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────┼─────────────────────────────────────┤
│ Distribution lists / groups │ PST distribution lists │ Planned   │ Deferred follow-up │ PidLidDistributionListMembers       │
└─────────────────────────────┴────────────────────────┴───────────┴────────────────────┴─────────────────────────────────────┘
```

---

## ☑️ Tasks

_Thunderbird to-dos → PST tasks_

**Status:** Full fidelity, including recurring tasks and attachments. Item relations are preserved in the body (no native Outlook slot).

```
┌─────────────────────────────┬────────────────────────┬───────────┬────────────────────────────────────┬─────────────────────────────┐
│ Source field                │ Goes to                │ Status    │ Notes / Why                        │ MAPI property               │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Title                       │ Task subject           │ Converted │                                    │ PidTagSubject               │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Notes / description         │ Body                   │ Converted │                                    │ PidTagBody                  │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Due date                    │ Due date               │ Converted │                                    │ PidLidTaskDueDate           │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Start date                  │ Start date             │ Converted │                                    │ PidLidTaskStartDate         │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Completed flag              │ Status = complete      │ Converted │                                    │ PidLidTaskComplete / Status │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Percent complete            │ % complete             │ Converted │                                    │ PidLidPercentComplete       │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Priority                    │ Importance             │ Converted │                                    │ PidTagImportance            │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Categories                  │ Categories             │ Converted │                                    │ PidNameKeywords             │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Reminders                   │ Reminder               │ Converted │                                    │ PidLidReminderSet           │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Recurring tasks             │ Recurring task series  │ Converted │ Master pattern; exceptions degrade │ PidLidTaskRecurrence        │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Task attachments            │ PST attachments        │ Converted │ Inline embedded; links in body     │ (attachment table)          │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Item relations (RELATED-TO) │ Preserved note in body │ Partial   │ No native Outlook relation slot    │ PidTagBody                  │
├─────────────────────────────┼────────────────────────┼───────────┼────────────────────────────────────┼─────────────────────────────┤
│ Assignees / task attendees  │ Task assignment        │ Planned   │ Deferred follow-up                 │ —                           │
└─────────────────────────────┴────────────────────────┴───────────┴────────────────────────────────────┴─────────────────────────────┘
```

---

## 🎨 Category colours

_Mail tags + calendar/task categories → baked-in Outlook master category list_

**Status:** Converted — colours are baked into the PST; rendering has one Outlook-side caveat (below).

Category *names* convert as shown in the tables above (mail tags, calendar and task categories → the item's
`Keywords`/categories). Their *colours* are baked into every converted PST as the Outlook **master category
list** — an `IPM.Configuration.CategoryList` hidden configuration item in a top-level "Calendar" folder — so
the file carries its own name→colour mapping with no Outlook automation, no COM, and no separate step. Each
colour is the one Thunderbird shows: your profile's colour override if set, otherwise Thunderbird's own
computed default. Every output PST carries the full union of categories across the conversion.

**Rendering caveat:** classic Outlook reads the master category list from your **primary/default** store only.
Open the converted PST as your primary store and categories render in colour; attach it as a *secondary* data
file and categorized items show their names but not their colours. See
**[how baked-in category colours work](HOW-CATEGORY-COLOURS-WORK.md)** for the mechanism and
**[Viewing category colours](VIEWING-CATEGORY-COLOURS.md)** for the steps. Classic Outlook on Windows only.

### Gmail labels from Google Takeout

`X-Gmail-Labels` is preserved without changing the underlying MIME message data. The default
`Compact` mode writes exactly one physical PST item, assigns every source label as an Outlook
category (`PidNameKeywords`), and chooses one label for the folder hierarchy. Selection is
deterministic: `gmailPrimaryLabelPriority` first, then the first nested label, then the first label
in the source header. Slash-separated labels become nested PST folders.

`ExactFolders` instead writes the message into every label folder. It retains the same Message-ID,
addresses, dates, body and attachments in each physical copy, but the report emits an explicit
space-amplification warning and counts planned copies. `Off` leaves pre-existing folder behavior
unchanged and does not add Gmail-label categories.

Warnings are emitted only for a source value that cannot be represented unchanged (for example an
adjusted folder name, a collision after adjustment, or an Outlook category longer than 255
characters). Empty label headers are not warned. Phase 1 does not create Outlook Search Folders;
category-based filtering remains available without duplicating messages.
