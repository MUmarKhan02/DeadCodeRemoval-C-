using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadCodeEliminator;

/// <summary>
/// Applies fixes to source code based on detected issues.
/// Line removal is done in reverse order to preserve indices.
/// </summary>
public class DeadCodeElim
{
    private readonly CliOptions _opts;
    private readonly ConsolePrinter _printer;

    public DeadCodeElim(CliOptions opts, ConsolePrinter printer)
    {
        _opts = opts;
        _printer = printer;
    }

    public string Eliminate(string source, ScanResult result)
    {
        var lines = source.Split('\n').ToList();

        // Collect all line indices to remove (from non-warning-only issues)
        var toRemove = new HashSet<int>();
        foreach (var issue in result.Issues)
        {
            if (issue.IsWarningOnly) continue;
            foreach (int lineIdx in issue.LinesToRemove)
                toRemove.Add(lineIdx);
        }

        if (toRemove.Count == 0) return source;

        // Remove in reverse order so indices stay valid
        foreach (int idx in toRemove.OrderByDescending(x => x))
        {
            if (idx < lines.Count)
                lines.RemoveAt(idx);
        }

        // Remove trailing blank lines that might be left
        string result2 = string.Join('\n', lines);
        result2 = RemoveConsecutiveBlankLines(result2);

        return result2;
    }

    private static string RemoveConsecutiveBlankLines(string source)
    {
        // Replace 3+ consecutive blank lines with 2
        while (source.Contains("\n\n\n"))
            source = source.Replace("\n\n\n", "\n\n");
        return source;
    }
}
