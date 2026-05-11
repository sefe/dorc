---
name: git-push-ticket
description: 'Auto-detect ticket from conversation, create feature branch from main, commit all related changes, and push. Use when: user says push ticket, push changes, push this, commit and push, create branch and push, or any variation of pushing work for a ticket.'
argument-hint: 'Optional: ticket number and title. If omitted, auto-detect from conversation context.'
---

# Git Push Ticket

Fully automated: detect the ticket from conversation context, create a branch off main, stage the right files, commit, and push.

## When to Use

- User says "push ticket", "push this", "push changes", "commit and push"
- After completing work for a GitHub issue/ticket
- Any time the user wants their current work pushed for a ticket

## Procedure

### 1. Detect the ticket

Look through the **conversation history** for:
- A ticket/issue number (e.g. `#566`, `ticket 566`, `issue #566`)
- A ticket title or description that was discussed

If multiple tickets are referenced, use the **most recent** one. If no ticket number is found, ask the user.

### 2. Determine the branch name

Generate: `feature/<number>-<slug>` where `<slug>` is the ticket title lowercased, hyphen-separated, max ~60 chars.

Example: `#566 Prevent special symbols in component names` → `feature/566-prevent-special-symbols-in-component-names`

### 3. Check current branch

Run `git branch --show-current`. 

- **If already on the correct feature branch**: Skip to step 5.
- **If on a different branch**: Continue to step 4.

### 4. Create branch off main

```bash
git stash
git fetch origin main
git checkout -b feature/<number>-<slug> origin/main
git stash pop
```

### 5. Detect and stage files

Run `git status --short` and `git diff --name-only` to see all modified files.

**Auto-detect relevant files**: Include files that were created or modified as part of the current task based on conversation history. Exclude files that:
- Were modified before this conversation started (unrelated changes)
- Are config files with secrets or local settings (e.g. `appsettings.json`)
- Are `.bak` files or temporary artifacts

Stage the relevant files with `git add <file1> <file2> ...`. Never use `git add .` or `git add -A`.

### 6. Commit with one-line message

Format: `#<number> <Short imperative description>`

Generate the commit message automatically from the ticket title. Keep it under 72 characters.

```bash
git commit -m "#566 Validate component name characters"
```

### 7. Push

```bash
git push origin feature/<number>-<slug>
```

### 8. Report

Show the PR creation URL from the push output. Done.

## Rules

- **Fully automatic**: Do not ask the user for confirmation at each step. Just do it.
- **Branch naming**: `feature/<number>-<slug>` — always off `origin/main`
- **Commit message**: One line only. Start with `#<number>`. Under 72 chars. No multi-line body.
- **Selective staging**: Never `git add .`. Only add files related to the ticket.
- **No force push**: Never use `--force` or `--force-with-lease`.
- **No destructive ops without asking**: Confirm with user before `git reset --hard`, `git clean`, or discarding changes.
