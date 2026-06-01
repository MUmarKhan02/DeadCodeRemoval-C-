using System.Collections.Generic;

namespace DeadCodeEliminator;

public class CliOptions
{
    public string? InputFile { get; set; }
    public string? InputFolder { get; set; }
    public string? OutputFile { get; set; }
    public bool DryRun { get; set; }
    public bool NoColor { get; set; }
    public bool ShowReport { get; set; }
    public bool Verbose { get; set; }
    public bool Recursive { get; set; } = true;  // folder scan is recursive by default
    public bool KeepUnreachableCode { get; set; }
    public bool KeepUnusedVariables { get; set; }
    public bool KeepUnusedUsings { get; set; }
    public bool KeepCommentedCode { get; set; }
    public bool KeepUnusedPrivateMethods { get; set; }

    public string? HtmlReportPath { get; set; }
    public bool IsFolderMode => InputFolder != null;
}

public enum IssueKind
{
    UnusedUsing,
    UnreachableCode,
    UnusedVariable,
    CommentedCode,
    UnusedPrivateMethod,
    UnusedPrivateField,
    EmptyBlock,
    DeadConditional,         // if (false) / if (true) else branch
    RedundantAssignment,
}

public class ContextLine
{
    public int LineNumber { get; set; }   // 1-indexed
    public string Content { get; set; } = "";
    public bool IsHighlighted { get; set; }  // true = the actual issue line(s)
}

public class CodeIssue
{
    public IssueKind Kind { get; set; }
    public int LineStart { get; set; }
    public int LineEnd { get; set; }
    public string Description { get; set; } = "";
    public string LineContent { get; set; } = "";
    /// <summary>Surrounding lines for context display (2 before, issue lines, 2 after)</summary>
    public List<ContextLine> Context { get; set; } = new();
    /// <summary>Lines to remove (0-indexed line numbers)</summary>
    public List<int> LinesToRemove { get; set; } = new();
    /// <summary>If true this is advisory only, not auto-fixable</summary>
    public bool IsWarningOnly { get; set; }
}

public class ScanResult
{
    public List<CodeIssue> Issues { get; set; } = new();
    public int TotalLines { get; set; }
    public Dictionary<IssueKind, int> CountByKind { get; set; } = new();
}

public class FolderScanResult
{
    public List<(string FilePath, ScanResult Result)> FileResults { get; set; } = new();
    public int TotalFiles => FileResults.Count;
    public int FilesWithIssues => FileResults.Count(r => r.Result.Issues.Count > 0);
    public int TotalIssues => FileResults.Sum(r => r.Result.Issues.Count);
    public int TotalAutoFix => FileResults.Sum(r => r.Result.Issues.Count(i => !i.IsWarningOnly));
    public int TotalAdvisory => FileResults.Sum(r => r.Result.Issues.Count(i => i.IsWarningOnly));
    public int TotalLines => FileResults.Sum(r => r.Result.TotalLines);
}