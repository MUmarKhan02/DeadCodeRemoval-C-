using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeadCodeEliminator;

public class DeadCodeCli
{
    private const string Version = "1.1.0";

    public int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        if (args[0] is "-v" or "--version")
        {
            Console.WriteLine($"deadcode v{Version}");
            return 0;
        }

        var options = ParseOptions(args);
        if (options == null) return 1;

        return options.IsFolderMode ? ExecuteFolder(options) : ExecuteFile(options);
    }

    private static CliOptions? ParseOptions(string[] args)
    {
        var opts = new CliOptions();
        int i = 0;

        while (i < args.Length)
        {
            switch (args[i])
            {
                case "-o" or "--output":
                    if (i + 1 >= args.Length) { Error("--output requires a path"); return null; }
                    opts.OutputFile = args[++i];
                    break;
                case "--dry-run":
                    opts.DryRun = true;
                    break;
                case "--no-color":
                    opts.NoColor = true;
                    break;
                case "--report":
                    opts.ShowReport = true;
                    break;
                case "--no-recurse":
                    opts.Recursive = false;
                    break;
                case "--keep-unreachable":
                    opts.KeepUnreachableCode = true;
                    break;
                case "--keep-unused-vars":
                    opts.KeepUnusedVariables = true;
                    break;
                case "--keep-unused-usings":
                    opts.KeepUnusedUsings = true;
                    break;
                case "--keep-commented":
                    opts.KeepCommentedCode = true;
                    break;
                case "--keep-unused-methods":
                    opts.KeepUnusedPrivateMethods = true;
                    break;
                case "--verbose":
                    opts.Verbose = true;
                    break;
                case "--report-html":
                    if (i + 1 >= args.Length) { Error("--report-html requires a file path"); return null; }
                    opts.HtmlReportPath = args[++i];
                    break;
                default:
                    if (args[i].StartsWith("-"))
                    {
                        Error($"Unknown option: {args[i]}");
                        return null;
                    }
                    if (opts.InputFile == null && opts.InputFolder == null)
                    {
                        string path = args[i];
                        if (Directory.Exists(path))
                            opts.InputFolder = path;
                        else if (File.Exists(path))
                            opts.InputFile = path;
                        else
                        {
                            Error($"Path not found: {path}");
                            return null;
                        }
                    }
                    else
                    {
                        Error("Multiple paths specified. Provide one file or one folder.");
                        return null;
                    }
                    break;
            }
            i++;
        }

        if (opts.InputFile == null && opts.InputFolder == null)
        {
            Error("No input file or folder specified.");
            PrintHelp();
            return null;
        }

        // --output doesn't make sense for folder mode
        if (opts.IsFolderMode && opts.OutputFile != null)
        {
            Error("--output cannot be used with a folder. Files are fixed in place (with .bak backups).");
            return null;
        }

        if (opts.InputFile != null &&
            !opts.InputFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            Warn("Input file does not have a .cs extension. Proceeding anyway.");
        }

        return opts;
    }

    // ── Single file mode ──────────────────────────────────────────────────────

    private static int ExecuteFile(CliOptions opts)
    {
        var printer = new ConsolePrinter(opts.NoColor);

        printer.Header($"Dead Code Eliminator v{Version}");
        printer.Info($"Scanning: {Path.GetFullPath(opts.InputFile!)}");
        Console.WriteLine();

        string source;
        try { source = File.ReadAllText(opts.InputFile!); }
        catch (Exception ex) { Error($"Could not read file: {ex.Message}"); return 1; }

        var scanner = new DeadCodeScanner(opts, printer);
        var result = scanner.Scan(source);

        if (result.Issues.Count == 0)
        {
            printer.Success("No dead code found. Your file looks clean!");
            return 0;
        }

        printer.PrintIssues(result.Issues, opts.Verbose);

        if (opts.ShowReport)
        {
            Console.WriteLine();
            printer.PrintReport(result);
        }

        if (opts.DryRun)
        {
            Console.WriteLine();
            printer.Warn("Dry run mode — no changes written.");

            if (opts.HtmlReportPath != null)
                WriteHtmlReport(opts, opts.InputFile!, result, printer);

            // Exit code 1 if auto-fixable issues found — allows CI pipelines to fail the build
            int autoFix = result.Issues.Count(i => !i.IsWarningOnly);
            return autoFix > 0 ? 1 : 0;
        }

        if (opts.HtmlReportPath != null)
            WriteHtmlReport(opts, opts.InputFile!, result, printer);

        return ApplyFixes(opts, opts.InputFile!, source, result, printer);
    }

    // ── Folder mode ───────────────────────────────────────────────────────────

    private static int ExecuteFolder(CliOptions opts)
    {
        var printer = new ConsolePrinter(opts.NoColor);
        string folderPath = Path.GetFullPath(opts.InputFolder!);

        printer.Header($"Dead Code Eliminator v{Version}");
        printer.Info($"Scanning folder: {folderPath}");
        printer.Info($"Recursive: {(opts.Recursive ? "yes" : "no (top level only)")}");
        Console.WriteLine();

        var searchOption = opts.Recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var csFiles = Directory.GetFiles(folderPath, "*.cs", searchOption)
            .OrderBy(f => f)
            .ToList();

        // Skip obj/ and bin/ folders — those are build artifacts
        csFiles = csFiles
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        if (csFiles.Count == 0)
        {
            printer.Warn("No .cs files found in the specified folder.");
            return 0;
        }

        printer.Info($"Found {csFiles.Count} .cs file(s)");
        Console.WriteLine();

        var folderResult = new FolderScanResult();
        var scanner = new DeadCodeScanner(opts, printer);

        foreach (string filePath in csFiles)
        {
            string relativePath = Path.GetRelativePath(folderPath, filePath);
            string source;

            try { source = File.ReadAllText(filePath); }
            catch (Exception ex)
            {
                printer.Warn($"Skipping {relativePath}: {ex.Message}");
                continue;
            }

            var result = scanner.Scan(source);
            folderResult.FileResults.Add((filePath, result));

            if (result.Issues.Count == 0)
            {
                printer.FileClean(relativePath);
            }
            else
            {
                printer.FileHeader(relativePath, result.Issues.Count);
                printer.PrintIssues(result.Issues, opts.Verbose);
            }
        }

        // Folder-level summary report (always shown in folder mode)
        Console.WriteLine();
        printer.PrintFolderReport(folderResult);

        if (opts.DryRun)
        {
            Console.WriteLine();
            printer.Warn("Dry run mode — no changes written.");

            if (opts.HtmlReportPath != null)
                WriteHtmlReport(opts, folderPath, folderResult, printer);

            // Exit code 1 if auto-fixable issues found — allows CI pipelines to fail the build
            return folderResult.TotalAutoFix > 0 ? 1 : 0;
        }

        if (opts.HtmlReportPath != null)
            WriteHtmlReport(opts, folderPath, folderResult, printer);

        if (folderResult.TotalAutoFix == 0)
        {
            Console.WriteLine();
            printer.Success("Nothing to fix automatically.");
            return 0;
        }

        // Apply fixes to each file that has auto-fixable issues
        Console.WriteLine();
        printer.Info("Applying fixes...");
        int fixedFiles = 0;

        foreach (var (filePath, result) in folderResult.FileResults)
        {
            if (!result.Issues.Any(i => !i.IsWarningOnly)) continue;

            string source = File.ReadAllText(filePath);
            int code = ApplyFixes(opts, filePath, source, result, printer);
            if (code == 0) fixedFiles++;
        }

        Console.WriteLine();
        printer.Success($"Done! Fixed {fixedFiles} file(s).");
        return 0;
    }

    // ── Shared fix logic ──────────────────────────────────────────────────────

    private static int ApplyFixes(CliOptions opts, string inputFile, string source,
                                   ScanResult result, ConsolePrinter printer)
    {
        var eliminator = new DeadCodeElim(opts, printer);
        string cleaned = eliminator.Eliminate(source, result);

        string outputPath = opts.OutputFile ?? inputFile;

        if (outputPath == inputFile)
        {
            string backup = inputFile + ".bak";
            File.Copy(inputFile, backup, overwrite: true);
            if (!opts.IsFolderMode)
                printer.Info($"Backup saved to: {backup}");
        }

        try { File.WriteAllText(outputPath, cleaned); }
        catch (Exception ex)
        {
            Error($"Could not write {outputPath}: {ex.Message}");
            return 1;
        }

        if (!opts.IsFolderMode)
        {
            Console.WriteLine();
            int removed = result.Issues.Count(x => !x.IsWarningOnly);
            printer.Success($"Done! Removed {removed} dead code issue(s). Written to: {outputPath}");
        }

        return 0;
    }

    // ── HTML report helpers ───────────────────────────────────────────────────

    private static void WriteHtmlReport(CliOptions opts, string filePath,
                                         ScanResult result, ConsolePrinter printer)
    {
        try
        {
            HtmlReporter.WriteReport(opts.HtmlReportPath!, filePath, result, Version);
            printer.Info($"HTML report written to: {Path.GetFullPath(opts.HtmlReportPath!)}");
        }
        catch (Exception ex)
        {
            Error($"Could not write HTML report: {ex.Message}");
        }
    }

    private static void WriteHtmlReport(CliOptions opts, string folderPath,
                                         FolderScanResult folderResult, ConsolePrinter printer)
    {
        try
        {
            HtmlReporter.WriteReport(opts.HtmlReportPath!, folderResult.FileResults, folderPath, Version);
            printer.Info($"HTML report written to: {Path.GetFullPath(opts.HtmlReportPath!)}");
        }
        catch (Exception ex)
        {
            Error($"Could not write HTML report: {ex.Message}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
deadcode - C# Dead Code Eliminator
Usage: deadcode [options] <file.cs>
       deadcode [options] <folder>

Options:
  -o, --output <path>       Write cleaned output to a different file (single file only)
      --dry-run             Scan only, do not modify files
      --report              Show a summary report after scanning
      --report-html <path>  Write a full HTML report to the given file
      --verbose             Show full line content for each issue
      --no-color            Disable colored output
      --no-recurse          Only scan the top-level folder, not subfolders
      --keep-unreachable    Keep unreachable code blocks (after return/throw)
      --keep-unused-vars    Keep unused local variables
      --keep-unused-usings  Keep unused 'using' directives
      --keep-commented      Keep commented-out code blocks
      --keep-unused-methods Keep unused private methods
  -v, --version             Show version
  -h, --help                Show this help

Examples:
  deadcode MyClass.cs
  deadcode --dry-run --report MyClass.cs
  deadcode -o MyClass.clean.cs MyClass.cs
  deadcode src/
  deadcode --dry-run --report src/
  deadcode --no-recurse --report src/
  deadcode --dry-run --report-html report.html src/
  deadcode --dry-run --report-html report.html MyClass.cs

Exit codes (dry-run mode):
  0  No auto-fixable issues found
  1  Auto-fixable issues found (use this to fail CI builds)
""");
    }

    private static void Error(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"error: {msg}");
        Console.ResetColor();
    }

    private static void Warn(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"warning: {msg}");
        Console.ResetColor();
    }
}