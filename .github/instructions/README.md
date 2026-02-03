# Bug Documentation for GitHub Copilot

This folder contains bug documentation that GitHub Copilot reads and updates.

## Files

- **copilot-instructions.md** - Instructions for Copilot on how to document bugs
- **bug-reports.instructions.md** - The actual bug database (Copilot appends here)
- **bug-template.instructions.md** - Quick reference template

## How It Works

1. GitHub Copilot fixes a bug in your code
2. Copilot reads `copilot-instructions.md` for the format
3. Copilot appends the documentation to `bug-reports.instructions.md`
4. Done!

## Manual Usage

If you want to document a bug yourself:

1. Copy the template from `bug-template.instructions.md`
2. Fill in the details
3. Append to `bug-reports.instructions.md` with `---` separator
4. Commit

That's it!
