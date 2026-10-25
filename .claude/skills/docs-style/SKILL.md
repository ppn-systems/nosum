---
name: docs-style
description: Use when writing or editing any markdown — README files, AGENTS.md, ARCHITECTURE.md, skills, ADRs. Language, ADR format, keeping agent docs lean, honest citations, no placeholder docs.
---

# Documentation style

## Language

Everything in this repository is **English**: READMEs, `AGENTS.md`, `ARCHITECTURE.md`, ADRs,
skills, code comments, XML docs. Player-facing game text is out of scope here.

## ADRs (`docs/adr/`)

- Required for any architecture or stack change.
- File `ADR-NNN-short-slug.md`; sections: status line, Context, Decision (numbered `D1…Dn`),
  Alternatives considered, Consequences, Revisit when. Format and index:
  `docs/adr/README.md` — add the index row in the same commit.
- An agent-written ADR lands as `Proposed`; it becomes `Accepted` when the owner approves it.
- Record the decision as it stands: no conversation quotes, no per-commit history.
- When a later ADR changes an earlier one, mark the earlier one `Superseded by ADR-NNN` in the
  same commit. Never delete an ADR.

## Agent-facing docs stay lean

- State a rule once, in its owning skill; `AGENTS.md` keeps a one-line summary and points to it.
- Rules and current facts only — no history or changelog.
- Do not hardcode counts or lists that drift; name the command or file that is the source of
  truth (`git ls-files '*.slnx'`).
- Point to code by path; one short example beats three.

## Citations

- Internal: repo-relative path (`engine/Nosum.Core/FixedTimestep.cs`). External: full URL plus
  access date.
- Never claim something was verified when it was not; mark assumptions `[Assumption]` with what
  happens if wrong.
- No placeholder docs (headings with no content). A README may say plainly that something is
  not implemented yet.
