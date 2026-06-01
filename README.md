# deadcode — C# Dead Code Eliminator CLI

A command-line tool that scans C# source files and project folders for dead code, with auto-fix, HTML reports with code snippets, dry-run mode, and CI exit codes.

---

## Requirements
- .NET 10.0 SDK or later

## Installation (Global Tool)

Install once and use `deadcode` from any folder on your machine:

```bash
dotnet pack
dotnet tool install --global --add-source ./bin/Release deadcode-eliminator
```

Then use it anywhere:

```bash
deadcode --version
deadcode --dry-run --report .
```

**To update after changes:**

```bash
dotnet pack
dotnet tool update --global --add-source ./bin/Release deadcode-eliminator
```

**To uninstall:**

```bash
dotnet tool uninstall --global deadcode-eliminator
```

## Build & Run (without installing)

```bash
dotnet build
dotnet run -- [options] <file.cs>
dotnet run -- [options] <folder>
```

Or publish a binary:

```bash
dotnet publish -c Release -o ./out
./out/deadcode [options] <file.cs>
```

---

## What It Detects

| Category | Auto-fix | Description |
|---|---|---|
| Unused `using` directives | ✅ | Imports never referenced in code |
| Unreachable code | ✅ | Statements after `return`/`throw`/`break`/`continue` |
| Unused local variables | ✅ | Variables declared but never read |
| Commented-out code | ✅ | Commented-out statements (not doc-comments) |
| Unused private fields | ✅ | Private fields never referenced |
| Redundant assignments | ✅ | Variable assigned then overwritten before being read |
| Unused private methods | ⚠️ | Private methods never called (advisory) |
| Empty catch/else blocks | ⚠️ | Empty blocks with no logic (advisory) |
| Dead conditionals | ⚠️ | `if (false)` / `if (true)` (advisory) |

Auto-fix issues are removed automatically. Advisory issues are flagged but left for manual review.

---

## Usage Examples

```bash
# Scan a single file and fix in place (creates .bak backup)
deadcode MyClass.cs

# Scan an entire project folder recursively
deadcode src/

# Dry run — show issues without writing anything
deadcode --dry-run --report --verbose MyClass.cs

# Generate an HTML report
deadcode --dry-run --report-html report.html .

# Write cleaned output to a new file
deadcode -o MyClass.clean.cs MyClass.cs

# Top-level folder only, no subfolders
deadcode --no-recurse --report src/

# Suppress specific checks
deadcode --keep-unused-vars --keep-commented MyClass.cs

# No ANSI colors (for CI or piped output)
deadcode --no-color --dry-run --report MyClass.cs
```

---

## All Options

```
  -o, --output <path>       Write cleaned output to a different file (single file only)
      --dry-run             Scan only, do not modify files
      --report              Show a summary report after scanning
      --report-html <path>  Write a full HTML report to the given file
      --verbose             Show code snippet context for each issue
      --no-color            Disable colored output
      --no-recurse          Only scan top-level folder, not subfolders
      --keep-unreachable    Skip unreachable code checks
      --keep-unused-vars    Skip unused local variable checks
      --keep-unused-usings  Skip unused 'using' directive checks
      --keep-commented      Skip commented-out code checks
      --keep-unused-methods Skip unused private method/field checks
  -v, --version             Show version
  -h, --help                Show help
```

---

## Exit Codes (dry-run mode)

| Code | Meaning |
|---|---|
| `0` | No auto-fixable issues found |
| `1` | Auto-fixable issues found |

Useful for CI pipelines — the build fails automatically if dead code is committed:

```yaml
- name: Check for dead code
  run: deadcode --dry-run .
```

---

## HTML Report

Run with `--report-html report.html` to generate a self-contained HTML report including:

- Summary cards (files scanned, total issues, auto-fixable vs advisory)
- Issues by category bar chart
- Top offending files chart
- Per-file collapsible sections with issue tables
- Code snippets with 2 lines of context above and below each issue

---

## Sample Test File

A `DeadCodeSample.cs` file is included in the repo containing 43 intentional dead code issues across all categories — useful for testing the tool.

```bash
deadcode --dry-run --report --verbose DeadCodeSample.cs
```
