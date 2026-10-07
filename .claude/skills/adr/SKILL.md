---
name: adr
description: Create a new Architecture Decision Record in docs/adr/ from the template. Use when a non-trivial technical decision is made or the owner asks to record a decision.
---

# Create an ADR

1. List `docs/adr/` and take the next free number (4 digits, e.g. `0003`).
2. Copy `docs/adr/0000-template.md` to `docs/adr/<number>-<kebab-case-title>.md`.
3. Fill every section in English: Context, Options considered (at least two, with real downsides), Decision, Consequences (including what gets harder and when to revisit), Interview angle.
4. Set Status to `accepted` only after the owner confirmed the decision; otherwise `proposed`.
5. Never edit the ADR of a past stage. If a decision changes, write a new ADR and set the old one to `superseded by ADR <number>`.
6. Add the question from "Interview angle" to the current stage in `docs/interview-notes.md`.
7. Commit as `docs: add ADR <number> <title>`.

After writing, ask the owner "why not <the most obvious alternative>?" and record weak answers in `docs/interview-notes.md`.
