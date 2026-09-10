# Wrapper handoff: Gmail labels (phase 1)

The converter now reads every `X-Gmail-Labels` header. No wrapper-side parsing is needed.

## Configuration

Each `outputs[]` object accepts:

```json
{
  "gmailLabelMode": "Compact",
  "gmailPrimaryLabelPriority": ["INBOX"]
}
```

- `Compact` is the default: one physical PST item, all labels as Outlook categories, one label chosen as the folder.
- `ExactFolders`: one physical copy per label folder. Before starting, show a prominent confirmation that output size and conversion time can increase substantially.
- `Off`: legacy behavior; Gmail labels do not affect folders or categories.
- `gmailPrimaryLabelPriority` is optional and only affects the primary-folder choice in `Compact` mode.

Do not infer a size multiplier in the wrapper. The final report supplies the exact planned-copy counts seen during conversion.

## Completed event and report

The `done` JSONL event adds:

```json
{
  "gmailLabels": {
    "sourcesDetected": 1,
    "sourceMessages": 100,
    "labeledMessages": 95,
    "messagesWithoutLabels": 5,
    "labelAssignments": 240,
    "pstCopiesPlanned": 240,
    "duplicateCopiesPlanned": 140
  },
  "gmailLabelNames": ["INBOX", "Clienti/Acme"]
}
```

In `ExactFolders`, `converted` counts physical PST items and may therefore exceed the scanned source-message total. In `Compact`, it remains one physical item per source message.

## Warning presentation

Group warnings by their bracketed code and show the supplied message as the cause. Relevant codes:

- `integrity:gmail-label-exact-mode-space`: configuration-level warning before conversion.
- `integrity:gmail-label-exact-mode-copy-count`: actual source messages, PST copies and additional copies after parsing.
- `integrity:gmail-labels-unparsed`: a non-empty source header produced no usable labels.
- `integrity:gmail-label-folder-adjusted`: a folder segment had to be changed for PST compatibility.
- `integrity:gmail-label-folder-collision`: distinct source labels became the same PST path.
- `integrity:gmail-label-folder-unrepresentable`: no safe folder path could be produced.
- `integrity:gmail-label-folder-depth`: the label exceeded the supported hierarchy depth.
- `integrity:gmail-label-category-adjusted`: a category exceeded Outlook's 255-character limit and was deterministically shortened.
- `integrity:gmail-label-category-unrepresentable`: no safe category value could be produced.

Empty/missing source labels intentionally produce no warning. Search Folder creation is not part of phase 1.
