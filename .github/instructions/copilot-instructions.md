# Bug Documentation Instructions for GitHub Copilot

## Purpose
When you fix a bug, append documentation to `bug-reports.instructions.md` in this directory.

## When to Document
- Bug took >1 hour to diagnose
- Non-obvious root cause
- Architectural decision made
- Could happen again

## Template Format

Use this format when appending to bug-reports.instructions.md:

```markdown
---

## BUG-XXX: [Brief Title]

**Date:** YYYY-MM-DD  
**Severity:** ?? Critical | ?? High | ?? Medium | ?? Low  
**Component:** [Component Name]

### Problem
[What was broken - clear description]

### Root Cause
[Technical explanation with code if relevant]

### Solution
[What was changed]

**Files Modified:**
- `path/to/file.ext` - brief change description

### Prevention
[How to avoid this in the future]
```

## Severity Guide
- ?? **Critical** - Feature completely broken
- ?? **High** - Major functionality impaired
- ?? **Medium** - Minor issue with workaround
- ?? **Low** - Cosmetic or rare edge case

## Example

```markdown
---

## BUG-002: File Upload Timeout on Large Files

**Date:** 2024-01-20  
**Severity:** ?? High  
**Component:** File Upload

### Problem
File uploads >10MB would timeout after 30 seconds and fail silently.

### Root Cause
Default HttpClient timeout was 30 seconds. Large files exceeded this on slower connections.

### Solution
Increased timeout to 5 minutes for upload operations.

**Files Modified:**
- `Services/FileUploadService.cs` - Set HttpClient.Timeout = TimeSpan.FromMinutes(5)

### Prevention
- Configure timeouts based on expected file sizes
- Add progress feedback for long operations
- Consider chunked uploads for large files
```

## Workflow

1. Fix the bug
2. Copy the template above
3. Fill in the details
4. Append to `bug-reports.instructions.md`
5. Use format: `---` separator, then your bug section

That's it! Keep it simple.
