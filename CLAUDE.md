# SIS on Tracto

Created: 2026-11-27 | Last updated: 2026-11-27

This branch (`sis`) is the SIS client project: the SIS payment-advice automation, built today in UiPath, rebuilt on Tracto.

## Read first

1. `tracto/docs/SIS/README.md`: the five flows, how each becomes a Tracto main job and a Go process, the daily chain, and the files supplied.
2. `tracto/docs/SIS/SIS-01` to `SIS-05`: one document per flow, with what the UiPath flow does, its shape in Tracto, what to build and its open questions.
3. `tracto/docs/SIS/stories/README.md`: the backlog (stories, acceptance criteria, order of work).
4. `tracto/docs/SIS/build-defaults.md`: the default to follow for every question not yet answered. `open-questions.md` lists the questions themselves.
5. `tracto/CLAUDE.md`: how Tracto works (orchestrator, runtimes, script format, publishing, jobs).

## How a UiPath flow becomes Tracto

- Each flow is a **schedule**, a **one-step main job** (`http.post` to a Go endpoint that ends at once) and a **Go process** that loops over entities, files or documents, with a status and reason per item. A rerun handles only unfinished items.
- No step drives a window or a web page: the desktop runtime only runs the one-step main jobs, and the browser runtime is not used.
- Configuration a person edits stays in workbooks, read through the sheet service. Outputs stay the same workbooks in the same folders, because the existing Python collection accounting service loads them (`collection-accounting-handoff.md`).
- PDF advices go through the existing extraction service (`extraction-service.md`) instead of UiPath Document Understanding.

## Rules for this project

- Shared Tracto changes (anything every client needs) are made on `general-dev` in `H:\Neelitech\Slim_RPA`, merged into `general` after testing, then merged into `sis`. Only SIS-specific work is committed here.
- The UiPath packages and configuration workbooks contain credentials. Never copy a credential value into a document, script, log or commit.
- Project documents go in `tracto/docs/SIS/`, each with a `Created | Last updated` date line under its title.
