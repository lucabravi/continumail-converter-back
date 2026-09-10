# TODO

## Native PST Search Folders for Gmail labels

- Keep `Compact` as the safe default: one physical PST item and all Gmail labels stored as Outlook categories.
- Prototype native Search Folders inside the generated PST without Outlook/COM, by extending the bundled `PSTFileFormat` library.
- Implement and validate search-folder node creation, Search Root hierarchy insertion, category restriction serialization, folder scope, Search Criteria Object, Search Domain Object registration, and update queues/tables.
- Prove with a minimal PST that results are references rather than message copies, persist after reopen, update after adding messages, and open cleanly in classic Outlook.
- Retain the optional Outlook COM post-processing approach only as a fallback if native folders are not portable or Outlook rebuilds/ignores them.

This is intentionally deferred until the current conversion-fidelity and Gmail-label work is complete.
