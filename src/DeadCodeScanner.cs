using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeadCodeEliminator;

/// <summary>
/// Scans a C# source file for dead code patterns using multi-pass text analysis.
/// Detects: unused usings, unreachable code, unused variables, commented-out code,
/// unused private methods/fields, empty catch/finally, dead conditionals.
/// </summary>
public class DeadCodeScanner
{
    private readonly CliOptions _opts;
    private readonly ConsolePrinter _printer;

    public DeadCodeScanner(CliOptions opts, ConsolePrinter printer)
    {
        _opts = opts;
        _printer = printer;
    }

    public ScanResult Scan(string source)
    {
        var lines = source.Split('\n');
        var result = new ScanResult { TotalLines = lines.Length };

        if (!_opts.KeepUnusedUsings)
            ScanUnusedUsings(lines, result);

        if (!_opts.KeepUnreachableCode)
            ScanUnreachableCode(lines, result);

        if (!_opts.KeepUnusedVariables)
            ScanUnusedVariables(lines, result);

        if (!_opts.KeepCommentedCode)
            ScanCommentedCode(lines, result);

        if (!_opts.KeepUnusedPrivateMethods)
        {
            ScanUnusedPrivateMethods(lines, result);
            ScanUnusedPrivateFields(lines, result);
        }

        ScanEmptyBlocks(lines, result);
        ScanDeadConditionals(lines, result);
        ScanRedundantAssignments(lines, result);

        // Build count summary
        foreach (var issue in result.Issues)
        {
            result.CountByKind.TryGetValue(issue.Kind, out int c);
            result.CountByKind[issue.Kind] = c + 1;
        }

        // Attach surrounding context lines to every issue
        AttachContext(lines, result);

        return result;
    }

    // ─── Pass 1: Unused usings ────────────────────────────────────────────────

    private static void ScanUnusedUsings(string[] lines, ScanResult result)
    {
        // Collect all using directives
        var usingPattern = new Regex(@"^\s*using\s+([\w.]+)\s*;", RegexOptions.Compiled);
        var usingStaticPattern = new Regex(@"^\s*using\s+static\s+([\w.]+)\s*;", RegexOptions.Compiled);
        var usingAliasPattern = new Regex(@"^\s*using\s+(\w+)\s*=\s*([\w.<>]+)\s*;", RegexOptions.Compiled);

        var usingLines = new List<(int lineIdx, string ns, string shortName)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("using ")) continue;
            if (line.StartsWith("using (")) continue; // using statements in code

            Match m;
            if ((m = usingAliasPattern.Match(line)).Success)
                usingLines.Add((i, m.Groups[1].Value, m.Groups[1].Value));
            else if ((m = usingStaticPattern.Match(line)).Success)
            {
                var parts = m.Groups[1].Value.Split('.');
                usingLines.Add((i, m.Groups[1].Value, parts[^1]));
            }
            else if ((m = usingPattern.Match(line)).Success)
            {
                var parts = m.Groups[1].Value.Split('.');
                usingLines.Add((i, m.Groups[1].Value, parts[^1]));
            }
        }

        if (usingLines.Count == 0) return;

        // Build usage body: everything after the using block
        int lastUsingLine = usingLines.Max(x => x.lineIdx);
        string body = string.Join("\n", lines.Skip(lastUsingLine + 1));

        foreach (var (lineIdx, ns, shortName) in usingLines)
        {
            // Skip System and Microsoft base namespaces used implicitly
            if (IsAlwaysUsedNamespace(ns)) continue;

            bool used = IsNamespaceUsed(body, ns, shortName);
            if (!used)
            {
                result.Issues.Add(new CodeIssue
                {
                    Kind = IssueKind.UnusedUsing,
                    LineStart = lineIdx + 1,
                    LineEnd = lineIdx + 1,
                    Description = $"Unused using directive: '{lines[lineIdx].Trim()}'",
                    LineContent = lines[lineIdx].Trim(),
                    LinesToRemove = new List<int> { lineIdx }
                });
            }
        }
    }

    private static bool IsAlwaysUsedNamespace(string ns)
    {
        // These are handled by implicit usings in .NET 6+ but keep for safety
        return false;
    }

    private static bool IsNamespaceUsed(string body, string ns, string shortName)
    {
        // Check if any identifier from this namespace appears in the body
        // Strategy: look for the short name (last segment) as a word boundary match
        if (shortName.Length == 0) return true;

        // Check for direct usage of short name as type/class reference
        var identPattern = new Regex($@"\b{Regex.Escape(shortName)}\b");
        if (identPattern.IsMatch(body)) return true;

        // Check for attribute usage (e.g., [Serializable] from System)
        var attrPattern = new Regex($@"\[{Regex.Escape(shortName)}");
        if (attrPattern.IsMatch(body)) return true;

        return false;
    }

    // ─── Pass 2: Unreachable code (after return/throw/continue/break) ─────────

    private static void ScanUnreachableCode(string[] lines, ScanResult result)
    {
        var terminators = new Regex(@"^\s*(return|throw|continue|break)\b[^/]*;", RegexOptions.Compiled);
        var meaningfulCode = new Regex(@"^\s*[^/\s\{\}]", RegexOptions.Compiled);
        var closingBrace = new Regex(@"^\s*\}", RegexOptions.Compiled);
        var labelOrCase = new Regex(@"^\s*(case\s|default\s*:|\w+\s*:)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length - 1; i++)
        {
            var line = lines[i];
            if (!terminators.IsMatch(line)) continue;
            if (IsInComment(lines, i)) continue;

            // Collect unreachable lines until closing brace or end
            var unreachable = new List<int>();
            int j = i + 1;
            while (j < lines.Length)
            {
                var next = lines[j];
                if (closingBrace.IsMatch(next)) break;
                if (labelOrCase.IsMatch(next)) break;
                if (IsComment(next.Trim())) { j++; continue; }
                if (string.IsNullOrWhiteSpace(next)) { j++; continue; }
                if (meaningfulCode.IsMatch(next))
                    unreachable.Add(j);
                j++;
            }

            if (unreachable.Count > 0)
            {
                result.Issues.Add(new CodeIssue
                {
                    Kind = IssueKind.UnreachableCode,
                    LineStart = unreachable[0] + 1,
                    LineEnd = unreachable[^1] + 1,
                    Description = $"Unreachable code after '{line.Trim()}'",
                    LineContent = lines[unreachable[0]].Trim(),
                    LinesToRemove = unreachable,
                    IsWarningOnly = false
                });
                i = unreachable[^1]; // skip past
            }
        }
    }

    // ─── Pass 3: Unused local variables ───────────────────────────────────────

    private static void ScanUnusedVariables(string[] lines, ScanResult result)
    {
        // Match local variable declarations: Type varName = ...; or var varName = ...;
        var varDecl = new Regex(
            @"^\s*(?:var|(?:(?:int|string|bool|double|float|long|byte|char|object|decimal|uint|ulong|short|ushort|sbyte)\??)|(?:[A-Z]\w*))\s+(\w+)\s*(?:=|;)",
            RegexOptions.Compiled);

        // Track method scope (simplified: find blocks between { })
        var methods = ExtractMethodBodies(lines);

        foreach (var (startLine, endLine, _) in methods)
        {
            var declarations = new Dictionary<string, int>(); // name -> lineIdx

            for (int i = startLine; i <= endLine && i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (IsComment(trimmed)) continue;
                if (IsInComment(lines, i)) continue;

                var m = varDecl.Match(lines[i]);
                if (!m.Success) continue;

                string varName = m.Groups[1].Value;
                if (IsKeyword(varName)) continue;
                if (varName == "_") continue; // discard pattern

                declarations[varName] = i;
            }

            // For each declared variable, check if it's used elsewhere in the method.
            // We scan to end-of-file from the declaration point to catch variables
            // used deep in nested loops or lambdas that ExtractMethodBodies may clip.
            foreach (var (varName, declLine) in declarations)
            {
                bool used = false;
                var usagePattern = new Regex($@"\b{Regex.Escape(varName)}\b");

                for (int i = declLine + 1; i < lines.Length; i++)
                {
                    var trimmed = lines[i].Trim();
                    if (IsComment(trimmed)) continue;

                    if (usagePattern.IsMatch(lines[i]))
                    {
                        used = true;
                        break;
                    }

                    // Stop scanning once we've left the method's class scope (hit a new class/method at top indent)
                    if (i > endLine + 50) break;
                }

                if (!used)
                {
                    // Avoid false positives for out/ref parameters
                    string decl = lines[declLine].Trim();
                    if (decl.Contains("out ") || decl.Contains("ref ")) continue;

                    result.Issues.Add(new CodeIssue
                    {
                        Kind = IssueKind.UnusedVariable,
                        LineStart = declLine + 1,
                        LineEnd = declLine + 1,
                        Description = $"Unused local variable: '{varName}'",
                        LineContent = lines[declLine].Trim(),
                        LinesToRemove = new List<int> { declLine }
                    });
                }
            }
        }
    }

    // ─── Pass 4: Commented-out code blocks ────────────────────────────────────

    private static void ScanCommentedCode(string[] lines, ScanResult result)
    {
        // Patterns that suggest code (not documentation) inside comments
        var codePatterns = new[]
        {
            new Regex(@"//\s*(if|for|while|foreach|switch|return|throw|var |int |string |bool )", RegexOptions.Compiled),
            new Regex(@"//\s*\w+\s*\(", RegexOptions.Compiled),         // method call
            new Regex(@"//\s*\w+\s*=\s*[^=]", RegexOptions.Compiled),   // assignment
            new Regex(@"//\s*\w+\.\w+", RegexOptions.Compiled),          // member access
        };

        var docCommentPattern = new Regex(@"^\s*///", RegexOptions.Compiled);

        var codeCommentLines = new List<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            if (docCommentPattern.IsMatch(lines[i])) continue;
            if (!trimmed.StartsWith("//")) continue;

            bool looksLikeCode = codePatterns.Any(p => p.IsMatch(lines[i]));
            if (looksLikeCode)
                codeCommentLines.Add(i);
        }

        // Group consecutive commented-code lines into blocks
        if (codeCommentLines.Count == 0) return;

        var groups = GroupConsecutive(codeCommentLines);
        foreach (var group in groups)
        {
            result.Issues.Add(new CodeIssue
            {
                Kind = IssueKind.CommentedCode,
                LineStart = group[0] + 1,
                LineEnd = group[^1] + 1,
                Description = $"Commented-out code block ({group.Count} line{(group.Count > 1 ? "s" : "")})",
                LineContent = lines[group[0]].Trim(),
                LinesToRemove = group,
                IsWarningOnly = false
            });
        }
    }

    // ─── Pass 5: Unused private methods ───────────────────────────────────────

    private static void ScanUnusedPrivateMethods(string[] lines, ScanResult result)
    {
        var privateMethod = new Regex(
            @"^\s*private\s+(?:static\s+)?(?:async\s+)?(?:\w[\w<>\[\]?]*\s+)?(\w+)\s*\(",
            RegexOptions.Compiled);

        var methodsFound = new List<(string name, int lineIdx)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (IsComment(trimmed)) continue;

            var m = privateMethod.Match(lines[i]);
            if (!m.Success) continue;

            string name = m.Groups[1].Value;
            if (IsKeyword(name) || name == "Main" || name == "Dispose") continue;

            // Skip constructors (same name as class)
            string classNameFromContext = GetEnclosingClassName(lines, i);
            if (name == classNameFromContext) continue;

            methodsFound.Add((name, i));
        }

        // Build full source for searching
        string fullSource = string.Join("\n", lines);

        foreach (var (name, declLine) in methodsFound)
        {
            // Count occurrences: should appear more than once (the declaration itself)
            var pattern = new Regex($@"\b{Regex.Escape(name)}\b", RegexOptions.Compiled);
            var matches = pattern.Matches(fullSource);

            // The method definition line itself, so > 1 means it's referenced elsewhere
            int defCount = 0;
            // Count uses excluding the declaration line
            int usages = 0;
            foreach (Match match in matches)
            {
                int matchLine = fullSource[..match.Index].Count(c => c == '\n');
                if (matchLine == declLine)
                    defCount++;
                else
                    usages++;
            }

            if (usages == 0)
            {
                result.Issues.Add(new CodeIssue
                {
                    Kind = IssueKind.UnusedPrivateMethod,
                    LineStart = declLine + 1,
                    LineEnd = declLine + 1,
                    Description = $"Unused private method: '{name}'",
                    LineContent = lines[declLine].Trim(),
                    LinesToRemove = new List<int>(), // method body removal is complex
                    IsWarningOnly = true             // flag as warning, don't auto-remove
                });
            }
        }
    }

    // ─── Pass 6: Unused private fields ────────────────────────────────────────

    private static void ScanUnusedPrivateFields(string[] lines, ScanResult result)
    {
        var privateField = new Regex(
            @"^\s*private\s+(?:readonly\s+|static\s+|const\s+)*(?:\w[\w<>\[\]?]*)\s+(_?\w+)\s*(?:=|;)",
            RegexOptions.Compiled);

        string fullSource = string.Join("\n", lines);

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (IsComment(trimmed)) continue;

            var m = privateField.Match(lines[i]);
            if (!m.Success) continue;

            string fieldName = m.Groups[1].Value;
            if (IsKeyword(fieldName)) continue;

            var pattern = new Regex($@"\b{Regex.Escape(fieldName)}\b", RegexOptions.Compiled);
            var matches = pattern.Matches(fullSource);

            int usages = 0;
            foreach (Match match in matches)
            {
                int matchLine = fullSource[..match.Index].Count(c => c == '\n');
                if (matchLine != i) usages++;
            }

            if (usages == 0)
            {
                result.Issues.Add(new CodeIssue
                {
                    Kind = IssueKind.UnusedPrivateField,
                    LineStart = i + 1,
                    LineEnd = i + 1,
                    Description = $"Unused private field: '{fieldName}'",
                    LineContent = lines[i].Trim(),
                    LinesToRemove = new List<int> { i }
                });
            }
        }
    }

    // ─── Pass 7: Empty blocks ─────────────────────────────────────────────────

    private static void ScanEmptyBlocks(string[] lines, ScanResult result)
    {
        // catch {} / finally {} / else {} with only whitespace/comments inside
        var catchPattern = new Regex(@"^\s*(catch|finally)\s*(?:\([^)]*\))?\s*\{", RegexOptions.Compiled);
        var elsePattern = new Regex(@"^\s*else\s*\{", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length - 1; i++)
        {
            var line = lines[i];
            bool isCatch = catchPattern.IsMatch(line);
            bool isElse = elsePattern.IsMatch(line);

            if (!isCatch && !isElse) continue;
            if (IsComment(line.Trim())) continue;

            // Check if block is empty or has only whitespace/comments until }
            int j = i + 1;
            bool hasContent = false;
            while (j < lines.Length)
            {
                var inner = lines[j].Trim();
                if (inner == "}") break;
                if (!string.IsNullOrWhiteSpace(inner) && !IsComment(inner))
                {
                    hasContent = true;
                    break;
                }
                j++;
            }

            if (!hasContent && j < lines.Length)
            {
                string kind = isCatch ? "catch/finally" : "else";
                result.Issues.Add(new CodeIssue
                {
                    Kind = IssueKind.EmptyBlock,
                    LineStart = i + 1,
                    LineEnd = j + 1,
                    Description = $"Empty {kind} block — consider removing or adding a comment",
                    LineContent = line.Trim(),
                    LinesToRemove = new List<int>(),
                    IsWarningOnly = true
                });
            }
        }
    }

    // ─── Pass 8: Dead conditionals ────────────────────────────────────────────

    private static void ScanDeadConditionals(string[] lines, ScanResult result)
    {
        var ifFalse = new Regex(@"^\s*if\s*\(\s*false\s*\)", RegexOptions.Compiled);
        var ifTrue = new Regex(@"^\s*if\s*\(\s*true\s*\)", RegexOptions.Compiled);
        var constFalse = new Regex(@"^\s*if\s*\(\s*(0\s*==\s*1|1\s*==\s*0)\s*\)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (IsComment(line.Trim())) continue;

            if (ifFalse.IsMatch(line) || constFalse.IsMatch(line))
            {
                result.Issues.Add(new CodeIssue
                {
                    Kind = IssueKind.DeadConditional,
                    LineStart = i + 1,
                    LineEnd = i + 1,
                    Description = "Dead conditional: condition is always false",
                    LineContent = line.Trim(),
                    LinesToRemove = new List<int>(),
                    IsWarningOnly = true
                });
            }
            else if (ifTrue.IsMatch(line))
            {
                result.Issues.Add(new CodeIssue
                {
                    Kind = IssueKind.DeadConditional,
                    LineStart = i + 1,
                    LineEnd = i + 1,
                    Description = "Dead conditional: condition is always true (remove if wrapper)",
                    LineContent = line.Trim(),
                    LinesToRemove = new List<int>(),
                    IsWarningOnly = true
                });
            }
        }
    }

    // ─── Pass 9: Redundant assignments ────────────────────────────────────────

    private static void ScanRedundantAssignments(string[] lines, ScanResult result)
    {
        // Detect variable assigned then immediately reassigned without any read
        var assignPattern = new Regex(@"^\s*(\w+)\s*=\s*[^=].*;\s*$", RegexOptions.Compiled);
        var methods = ExtractMethodBodies(lines);

        foreach (var (startLine, endLine, _) in methods)
        {
            string? lastAssignedVar = null;
            int lastAssignLine = -1;
            bool wasRead = false;

            for (int i = startLine; i <= endLine && i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (IsComment(trimmed) || string.IsNullOrWhiteSpace(trimmed)) continue;

                var m = assignPattern.Match(lines[i]);
                if (m.Success)
                {
                    string varName = m.Groups[1].Value;
                    if (IsKeyword(varName)) continue;
                    // Skip declarations
                    if (lines[i].TrimStart().StartsWith("var ") || lines[i].TrimStart().StartsWith("int ")) continue;

                    // Before recording a new assignment, check if the variable is read
                    // on THIS line too (e.g. compound expressions like "if (x == y) x = z")
                    if (lastAssignedVar != null && !wasRead)
                    {
                        var preReadPattern = new Regex($@"\b{Regex.Escape(lastAssignedVar)}\b");
                        string trimmedCurrent = lines[i].TrimStart();
                        bool isPlainAssign = trimmedCurrent.StartsWith(lastAssignedVar + " =") ||
                                            trimmedCurrent.StartsWith(lastAssignedVar + "=");
                        if (!isPlainAssign && preReadPattern.IsMatch(lines[i]))
                            wasRead = true;
                    }

                    if (lastAssignedVar == varName && !wasRead)
                    {
                        // Final safety check: scan the whole method body for any read of this var.
                        // This prevents false positives when reads appear before the second assignment
                        // in the source (e.g. inside a preceding if-condition).
                        bool readAnywhereInMethod = false;
                        var safetyPattern = new Regex($@"\b{Regex.Escape(varName)}\b");
                        for (int k = startLine; k <= endLine && k < lines.Length; k++)
                        {
                            if (k == lastAssignLine || k == i) continue;
                            string kt = lines[k].TrimStart();
                            if (IsComment(kt)) continue;
                            // Must not be itself an assignment line for this var
                            if (kt.StartsWith(varName + " =") || kt.StartsWith(varName + "=")) continue;
                            if (safetyPattern.IsMatch(lines[k])) { readAnywhereInMethod = true; break; }
                        }

                        if (!readAnywhereInMethod)
                        {
                            result.Issues.Add(new CodeIssue
                            {
                                Kind = IssueKind.RedundantAssignment,
                                LineStart = lastAssignLine + 1,
                                LineEnd = lastAssignLine + 1,
                                Description = $"Redundant assignment: '{varName}' is assigned but never read before being reassigned",
                                LineContent = lines[lastAssignLine].Trim(),
                                LinesToRemove = new List<int> { lastAssignLine },
                                IsWarningOnly = false
                            });
                        }
                    }

                    lastAssignedVar = varName;
                    lastAssignLine = i;
                    wasRead = false;
                }
                else if (lastAssignedVar != null)
                {
                    // Count as a read if the variable appears anywhere on this line
                    // that isn't itself a plain assignment — covers if/while/return/method args
                    var readPattern = new Regex($@"\b{Regex.Escape(lastAssignedVar)}\b");
                    string trimmedLine = lines[i].TrimStart();
                    bool isPlainAssignment = trimmedLine.StartsWith(lastAssignedVar + " =") ||
                                            trimmedLine.StartsWith(lastAssignedVar + "=");
                    if (readPattern.IsMatch(lines[i]) && !isPlainAssignment)
                        wasRead = true;
                }
            }
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static List<(int start, int end, string name)> ExtractMethodBodies(string[] lines)
    {
        var results = new List<(int, int, string)>();
        var methodPattern = new Regex(
            @"^\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed)\s+)*\w[\w<>\[\]?]*\s+(\w+)\s*\(",
            RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var m = methodPattern.Match(lines[i]);
            if (!m.Success) continue;

            // Find opening brace
            int braceStart = i;
            while (braceStart < lines.Length && !lines[braceStart].Contains('{'))
                braceStart++;

            if (braceStart >= lines.Length) continue;

            // Find matching closing brace
            int depth = 0;
            int end = braceStart;
            for (int j = braceStart; j < lines.Length; j++)
            {
                foreach (char c in lines[j])
                {
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
                if (depth == 0) { end = j; break; }
            }

            if (end > braceStart)
                results.Add((braceStart + 1, end - 1, m.Groups[1].Value));
        }

        return results;
    }

    private static string GetEnclosingClassName(string[] lines, int methodLine)
    {
        var classPattern = new Regex(@"^\s*(?:public|private|protected|internal|static|abstract|sealed)?\s*class\s+(\w+)", RegexOptions.Compiled);
        for (int i = methodLine; i >= 0; i--)
        {
            var m = classPattern.Match(lines[i]);
            if (m.Success) return m.Groups[1].Value;
        }
        return "";
    }

    private static bool IsComment(string trimmedLine)
    {
        return trimmedLine.StartsWith("//") || trimmedLine.StartsWith("/*") || trimmedLine.StartsWith("*");
    }

    private static bool IsInComment(string[] lines, int lineIdx)
    {
        // Simplified: check if we're inside a /* */ block
        bool inBlock = false;
        for (int i = 0; i < lineIdx; i++)
        {
            if (lines[i].Contains("/*")) inBlock = true;
            if (lines[i].Contains("*/")) inBlock = false;
        }
        return inBlock;
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "else", "for", "foreach", "while", "do", "switch", "case", "return",
        "break", "continue", "throw", "try", "catch", "finally", "using", "namespace",
        "class", "interface", "struct", "enum", "new", "this", "base", "null", "true",
        "false", "void", "var", "int", "string", "bool", "double", "float", "long",
        "byte", "char", "object", "decimal", "uint", "ulong", "short", "ushort", "sbyte",
        "public", "private", "protected", "internal", "static", "readonly", "const",
        "override", "virtual", "abstract", "sealed", "async", "await", "yield", "get",
        "set", "value", "in", "out", "ref", "params", "is", "as", "typeof", "sizeof",
        "default", "delegate", "event", "lock", "checked", "unchecked", "unsafe", "fixed"
    };

    private static bool IsKeyword(string name) => Keywords.Contains(name);

    private static List<List<int>> GroupConsecutive(List<int> lineNums)
    {
        var groups = new List<List<int>>();
        if (lineNums.Count == 0) return groups;

        var current = new List<int> { lineNums[0] };
        for (int i = 1; i < lineNums.Count; i++)
        {
            if (lineNums[i] == lineNums[i - 1] + 1)
                current.Add(lineNums[i]);
            else
            {
                groups.Add(current);
                current = new List<int> { lineNums[i] };
            }
        }
        groups.Add(current);
        return groups;
    }

    /// <summary>
    /// Attaches surrounding context lines to every issue in the result.
    /// Call once after all scan passes are complete.
    /// </summary>
    public static void AttachContext(string[] lines, ScanResult result, int contextSize = 2)
    {
        foreach (var issue in result.Issues)
        {
            // LineStart/LineEnd are 1-indexed
            int startIdx = issue.LineStart - 1;   // 0-indexed
            int endIdx = issue.LineEnd - 1;

            int from = Math.Max(0, startIdx - contextSize);
            int to = Math.Min(lines.Length - 1, endIdx + contextSize);

            for (int i = from; i <= to; i++)
            {
                issue.Context.Add(new ContextLine
                {
                    LineNumber = i + 1,
                    Content = lines[i],
                    IsHighlighted = i >= startIdx && i <= endIdx
                });
            }
        }
    }
}