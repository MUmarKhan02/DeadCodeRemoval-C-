# deadcode — C# Dead Code Eliminator CLI

A command-line tool that scans a C# source file and eliminates dead code with
auto-fix and advisory diagnostics.

## Requirements
- .NET 8.0 SDK or later

## Build & Run

```bash
dotnet build
dotnet run -- [options] <file.cs>
```

Or publish a binary:

```bash
dotnet publish -c Release -o ./out
./out/deadcode [options] <file.cs>
```

## What It Detects

| Category | Auto-fix | Description |
|---|---|---|
| Unused `using` directives | ✅ | Imports never referenced in code |
| Unreachable code | ✅ | Statements after `return`/`throw`/`break`/`continue` |
| Unused local variables | ✅ | Variables declared but never read |
| Commented-out code | ✅ | Commented-out statements (not doc-comments) |
| Unused private fields | ✅ | Private fields never referenced |
| Redundant assignments | ✅ | Variable assigned then overwritten before being read |
| Unused private methods | ⚠️ | Private methods never called (advisory — body removal is manual) |
| Empty catch/else blocks | ⚠️ | Empty blocks with no logic |
| Dead conditionals | ⚠️ | `if (false)` / `if (true)` |

## Usage Examples

```bash
# Scan and fix in place (creates .bak backup)
deadcode MyClass.cs

# Dry run — show issues without writing
deadcode --dry-run --report --verbose MyClass.cs

# Write cleaned file to a new path
deadcode -o MyClass.clean.cs MyClass.cs

# Suppress specific checks
deadcode --keep-unused-vars --keep-commented MyClass.cs

# No ANSI colors (for CI/piped output)
deadcode --no-color --report MyClass.cs
```

## All Options

```
  -o, --output <path>       Write to a different file
      --dry-run             Scan only, don't write
      --report              Show summary report
      --verbose             Print line content per issue
      --no-color            Disable ANSI colors
      --keep-unreachable    Skip unreachable code checks
      --keep-unused-vars    Skip unused variable checks
      --keep-unused-usings  Skip unused using checks
      --keep-commented      Skip commented-out code checks
      --keep-unused-methods Skip unused private method checks
  -v, --version
  -h, --help
```
