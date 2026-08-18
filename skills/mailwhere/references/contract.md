# MailWhere skill contract

- `MailWhere.Cli.exe` is the only normal data source.
- JSON envelopes use `ok`, `data`, `code`, and `message`.
- `search-mail` results may include `open_source_token`; this is opaque and local-only.
- Do not expose raw mailbox locators: exclude `store_id`, `entry_id`, raw StoreID/EntryID, and `source_id`. Treat `open_source_token` as a one-purpose handle for `MailWhere.exe --open-source-token`.
- If the database or Outlook is unavailable, summarize the sanitized `code`/`message` and stop.
