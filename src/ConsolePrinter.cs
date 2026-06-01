using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadCodeEliminator;

public class ConsolePrinter
{
    private readonly bool _noColor;

    public ConsolePrinter(bool noColor)
    {
        _noColor = noColor;
    }

    public void Header(string text)
    {
        if (!_noColor) Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"┌─ {text} ─────────────────────────────────────");
        if (!_noColor) Console.ResetColor();
    }

    public void Info(string text)
    {
        if (!_noColor) Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"  {text}");
        if (!_noColor) Console.ResetColor();
    }

    public void Success(string text)
    {
        if (!_noColor) Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ {text}");
        if (!_noColor) Console.ResetColor();
    }

    public void Warn(string text)
    {
        if (!_noColor) Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠ {text}");
        if (!_noColor) Console.ResetColor();
    }

    public void PrintIssues(List<CodeIssue> issues, bool verbose)
    {
        // Group by kind
        var grouped = issues.GroupBy(i => i.Kind).OrderBy(g => g.Key.ToString());

        foreach (var group in grouped)
        {
            PrintKindHeader(group.Key, group.Count());

            foreach (var issue in group)
            {
                PrintIssue(issue, verbose);
            }

            Console.WriteLine();
        }
    }

    private void PrintKindHeader(IssueKind kind, int count)
    {
        string label = kind switch
        {
            IssueKind.UnusedUsing => "Unused Using Directives",
            IssueKind.UnreachableCode => "Unreachable Code",
            IssueKind.UnusedVariable => "Unused Local Variables",
            IssueKind.CommentedCode => "Commented-Out Code",
            IssueKind.UnusedPrivateMethod => "Unused Private Methods",
            IssueKind.UnusedPrivateField => "Unused Private Fields",
            IssueKind.EmptyBlock => "Empty Blocks",
            IssueKind.DeadConditional => "Dead Conditionals",
            IssueKind.RedundantAssignment => "Redundant Assignments",
            _ => kind.ToString()
        };

        if (!_noColor) Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  [{label}] — {count} issue{(count != 1 ? "s" : "")}");
        if (!_noColor) Console.ResetColor();
    }

    private void PrintIssue(CodeIssue issue, bool verbose)
    {
        string icon = issue.IsWarningOnly ? "⚠" : "✗";
        string lineRef = issue.LineStart == issue.LineEnd
            ? $"line {issue.LineStart}"
            : $"lines {issue.LineStart}–{issue.LineEnd}";

        ConsoleColor color = issue.IsWarningOnly
            ? ConsoleColor.Yellow
            : ConsoleColor.Red;

        if (!_noColor) Console.ForegroundColor = color;
        Console.Write($"    {icon} {lineRef}");
        if (!_noColor) Console.ResetColor();

        if (!_noColor) Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  ");
        if (!_noColor) Console.ResetColor();

        Console.Write(issue.Description);

        if (issue.IsWarningOnly)
        {
            if (!_noColor) Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("  [advisory]");
            if (!_noColor) Console.ResetColor();
        }
        else
        {
            if (!_noColor) Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Write("  [auto-fix]");
            if (!_noColor) Console.ResetColor();
        }

        Console.WriteLine();

        if (verbose && issue.Context.Count > 0)
        {
            foreach (var ctx in issue.Context)
            {
                string lineNum = ctx.LineNumber.ToString().PadLeft(5);
                string content = TruncateLine(ctx.Content, 90);

                if (ctx.IsHighlighted)
                {
                    if (!_noColor) Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"    ▶{lineNum} │ ");
                    if (!_noColor) Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(content);
                    if (!_noColor) Console.ResetColor();
                }
                else
                {
                    if (!_noColor) Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"      {lineNum} │ {content}");
                    if (!_noColor) Console.ResetColor();
                }
            }
            Console.WriteLine();
        }
    }

    public void PrintReport(ScanResult result)
    {
        if (!_noColor) Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ── Summary Report ────────────────────────────");
        if (!_noColor) Console.ResetColor();

        Console.WriteLine($"  Total lines scanned : {result.TotalLines}");
        Console.WriteLine($"  Total issues found  : {result.Issues.Count}");

        int autoFix = result.Issues.Count(i => !i.IsWarningOnly);
        int advisory = result.Issues.Count(i => i.IsWarningOnly);

        if (!_noColor) Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Auto-fixable        : {autoFix}");
        if (!_noColor) Console.ResetColor();

        if (!_noColor) Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  Advisory (manual)   : {advisory}");
        if (!_noColor) Console.ResetColor();

        Console.WriteLine();

        if (!_noColor) Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  By category:");
        if (!_noColor) Console.ResetColor();

        foreach (var (kind, count) in result.CountByKind.OrderByDescending(x => x.Value))
        {
            string label = kind.ToString().PadRight(25);
            Console.WriteLine($"    {label} {count}");
        }
    }

    public void FileHeader(string relativePath, int issueCount)
    {
        if (!_noColor) Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"┌─ {relativePath}  ({issueCount} issue{(issueCount != 1 ? "s" : "")})");
        if (!_noColor) Console.ResetColor();
    }

    public void FileClean(string relativePath)
    {
        if (!_noColor) Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"  ✓  {relativePath}");
        if (!_noColor) Console.ResetColor();
    }

    public void PrintFolderReport(FolderScanResult folder)
    {
        if (!_noColor) Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ── Folder Summary ────────────────────────────");
        if (!_noColor) Console.ResetColor();

        Console.WriteLine($"  Files scanned       : {folder.TotalFiles}");
        Console.WriteLine($"  Files with issues   : {folder.FilesWithIssues}");
        Console.WriteLine($"  Total lines scanned : {folder.TotalLines}");
        Console.WriteLine($"  Total issues found  : {folder.TotalIssues}");

        if (!_noColor) Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Auto-fixable        : {folder.TotalAutoFix}");
        if (!_noColor) Console.ResetColor();

        if (!_noColor) Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  Advisory (manual)   : {folder.TotalAdvisory}");
        if (!_noColor) Console.ResetColor();

        // Top offending files
        var worst = folder.FileResults
            .Where(r => r.Result.Issues.Count > 0)
            .OrderByDescending(r => r.Result.Issues.Count)
            .Take(5)
            .ToList();

        if (worst.Count > 0)
        {
            Console.WriteLine();
            if (!_noColor) Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  Most issues:");
            if (!_noColor) Console.ResetColor();

            foreach (var (filePath, result) in worst)
            {
                string name = Path.GetFileName(filePath).PadRight(35);
                Console.WriteLine($"    {name} {result.Issues.Count} issue(s)");
            }
        }
    }

    private static string TruncateLine(string line, int max)
        => line.Length <= max ? line : line[..max] + "…";
}