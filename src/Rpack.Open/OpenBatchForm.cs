using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Rpack.Core;

namespace Rpack.Open;

internal sealed class OpenBatchForm : Form
{
    private readonly ProcessRunner _processRunner;
    private readonly GitClient _gitClient;
    private readonly RpackPackageService _service;
    private readonly List<PackageJob> _jobs = [];
    private readonly ListView _list = new();
    private readonly TabControl _statusTabs = new();
    private readonly TabPage _detailsTab = new("Status / errors");
    private readonly TabPage _consoleLogTab = new("Console log");
    private readonly TextBox _details = new();
    private readonly TextBox _consoleLog = new();
    private readonly Label _summary = new();
    private readonly Button _recheckButton = new();
    private readonly Button _applyButton = new();
    private readonly Button _removeButton = new();
    private readonly Button _copyButton = new();
    private readonly CheckBox _ignoreSpaceChangeBox = new();
    private readonly System.Windows.Forms.Timer _checkTimer = new();
    private bool _checking;
    private bool _updatingWhitespaceBox;

    public OpenBatchForm()
    {
        _processRunner = new ProcessRunner(AppendProcessLog);
        _gitClient = new GitClient(_processRunner);
        _service = new RpackPackageService(_gitClient);
        Text = "rpack packages";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 620);
        Size = new Size(1120, 720);
        Icon = LoadAppIcon();

        BuildLayout();
        _checkTimer.Interval = 450;
        _checkTimer.Tick += (_, _) =>
        {
            _checkTimer.Stop();
            _ = CheckPendingAsync();
        };
    }

    public void AddRequest(OpenRequest request)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(() => AddRequest(request));
            }
            catch (InvalidOperationException)
            {
                return;
            }

            return;
        }

        foreach (var packagePath in request.PackagePaths)
        {
            var fullPath = Path.GetFullPath(packagePath);
            var existing = _jobs.FirstOrDefault(job => string.Equals(job.PackagePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.AllowDirty = existing.AllowDirty || request.AllowDirty;
                existing.DirtyReason = request.AllowDirty ? request.DirtyReason : existing.DirtyReason;
                existing.RepositoryOption = request.RepositoryPath ?? existing.RepositoryOption;
                existing.PathPrefix = request.PathPrefix ?? existing.PathPrefix;
                existing.StrictBase = existing.StrictBase || request.StrictBase;
                existing.IgnoreSpaceChange = request.StrictWhitespace
                    ? false
                    : existing.IgnoreSpaceChange || request.IgnoreSpaceChange || _ignoreSpaceChangeBox.Checked;
                if (existing.State is PackageState.Error)
                {
                    existing.State = PackageState.Pending;
                }

                continue;
            }

            _jobs.Add(new PackageJob
            {
                PackagePath = fullPath,
                RepositoryOption = request.RepositoryPath,
                PathPrefix = request.PathPrefix,
                AllowDirty = request.AllowDirty,
                DirtyReason = request.DirtyReason,
                StrictBase = request.StrictBase,
                IgnoreSpaceChange = request.StrictWhitespace
                    ? false
                    : request.IgnoreSpaceChange || _ignoreSpaceChangeBox.Checked
            });
        }

        _updatingWhitespaceBox = true;
        _ignoreSpaceChangeBox.Checked = _jobs.Count == 0 || _jobs.All(job => job.IgnoreSpaceChange);
        _updatingWhitespaceBox = false;
        RefreshList();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        BringToFront();
        Activate();
        _checkTimer.Stop();
        _checkTimer.Start();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _summary.Dock = DockStyle.Fill;
        _summary.TextAlign = ContentAlignment.MiddleLeft;

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.CheckBoxes = true;
        _list.HideSelection = false;
        _list.Columns.Add("Status", 90);
        _list.Columns.Add("Package", 210);
        _list.Columns.Add("Title", 220);
        _list.Columns.Add("Repo", 230);
        _list.Columns.Add("Patches", 70);
        _list.Columns.Add("Files", 60);
        _list.Columns.Add("Lines", 90);
        _list.Columns.Add("Hunks", 60);
        _list.Columns.Add("Mode", 120);
        _list.Columns.Add("Message", 420);
        _list.SelectedIndexChanged += (_, _) => RefreshDetails();
        _list.ItemChecked += (_, e) =>
        {
            if (e.Item.Tag is PackageJob job)
            {
                job.Selected = e.Item.Checked;
            }
        };

        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = false;
        _details.Font = new Font(FontFamily.GenericMonospace, 9);

        _consoleLog.Dock = DockStyle.Fill;
        _consoleLog.Multiline = true;
        _consoleLog.ReadOnly = true;
        _consoleLog.ScrollBars = ScrollBars.Both;
        _consoleLog.WordWrap = false;
        _consoleLog.Font = new Font(FontFamily.GenericMonospace, 9);

        _statusTabs.Dock = DockStyle.Fill;
        _detailsTab.Controls.Add(_details);
        _consoleLogTab.Controls.Add(_consoleLog);
        _statusTabs.TabPages.Add(_detailsTab);
        _statusTabs.TabPages.Add(_consoleLogTab);

        _ignoreSpaceChangeBox.Text = "Allow whitespace context match";
        _ignoreSpaceChangeBox.AutoSize = true;
        _ignoreSpaceChangeBox.Height = 30;
        _ignoreSpaceChangeBox.TextAlign = ContentAlignment.MiddleCenter;
        _ignoreSpaceChangeBox.Checked = true;
        _ignoreSpaceChangeBox.CheckedChanged += async (_, _) =>
        {
            if (_updatingWhitespaceBox)
            {
                return;
            }

            foreach (var job in _jobs.Where(job => job.State != PackageState.Applied))
            {
                job.IgnoreSpaceChange = _ignoreSpaceChangeBox.Checked;
                job.ResetForCheck();
            }

            RefreshList();
            await CheckPendingAsync();
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var closeButton = new Button { Text = "Close", Width = 110, Height = 30 };
        closeButton.Click += (_, _) => Close();

        _applyButton.Text = "Apply selected";
        _applyButton.Width = 130;
        _applyButton.Height = 30;
        _applyButton.Click += async (_, _) => await ApplySelectedAsync();

        _recheckButton.Text = "Recheck";
        _recheckButton.Width = 100;
        _recheckButton.Height = 30;
        _recheckButton.Click += async (_, _) =>
        {
            foreach (var job in _jobs.Where(job => job.State is not PackageState.Applied and not PackageState.Applying))
            {
                job.ResetForCheck();
            }

            RefreshList();
            await CheckPendingAsync();
        };

        _removeButton.Text = "Remove";
        _removeButton.Width = 100;
        _removeButton.Height = 30;
        _removeButton.Click += (_, _) => RemoveSelected();

        _copyButton.Text = "Copy details";
        _copyButton.Width = 110;
        _copyButton.Height = 30;
        _copyButton.Click += (_, _) =>
        {
            var text = _statusTabs.SelectedTab == _consoleLogTab ? _consoleLog.Text : _details.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                Clipboard.SetText(text);
            }
        };

        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(_applyButton);
        buttons.Controls.Add(_recheckButton);
        buttons.Controls.Add(_removeButton);
        buttons.Controls.Add(_copyButton);
        buttons.Controls.Add(_ignoreSpaceChangeBox);

        root.Controls.Add(_summary, 0, 0);
        root.Controls.Add(_list, 0, 1);
        root.Controls.Add(_statusTabs, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
    }

    private async Task CheckPendingAsync()
    {
        if (_checking)
        {
            _checkTimer.Stop();
            _checkTimer.Start();
            return;
        }

        _checking = true;
        SetButtons(enabled: false);
        try
        {
            while (true)
            {
                var pending = _jobs.FirstOrDefault(job => job.State == PackageState.Pending);
                if (pending is null)
                {
                    break;
                }

                pending.State = PackageState.Checking;
                RefreshList();
                await Task.Run(() => CheckJob(pending));
                RefreshList();
            }
        }
        finally
        {
            _checking = false;
            SetButtons(enabled: true);
            RefreshList();
        }
    }

    private void CheckJob(PackageJob job)
    {
        try
        {
            if (!File.Exists(job.PackagePath))
            {
                throw new FileNotFoundException(job.PackagePath);
            }

            var repositoryPath = ResolveRepositoryPath(job);
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                job.Fail(
                    "ResolveRepository",
                    new PackageProblem(
                        "Repository not found",
                        "rpack could not find a Git repository for this package.",
                        "Place the package inside the target repo or open with --repo <repo>.",
                        "No repository was found by walking upward from the package location."));
                return;
            }

            job.RepositoryPath = repositoryPath;
            var allowedDirtyPaths = GetPackageDirtyException(repositoryPath, job.PackagePath);
            var inspection = _service.Inspect(new InspectPackageOptions
            {
                PackagePath = job.PackagePath,
                PathPrefix = job.PathPrefix
            });
            var check = _service.Check(new CheckPackageOptions
            {
                PackagePath = job.PackagePath,
                RepositoryPath = repositoryPath,
                AllowDirty = job.AllowDirty,
                StrictBase = job.StrictBase,
                PathPrefix = job.PathPrefix,
                AllowedDirtyPaths = allowedDirtyPaths,
                IgnoreSpaceChange = job.IgnoreSpaceChange
            });

            job.Inspection = inspection;
            if (!check.Success)
            {
                job.Fail("Check", ErrorFormatter.FromCheckFailure(check.Message, job.AllowDirty, job.IgnoreSpaceChange));
                return;
            }

            job.State = check.Message.Contains("Warning:", StringComparison.OrdinalIgnoreCase)
                ? PackageState.Warning
                : PackageState.Ready;
            job.Message = check.Message;
            job.Details = BuildSuccessDetails(job, "Check", check.Message);
        }
        catch (Exception ex)
        {
            job.Fail("Inspect", ErrorFormatter.SummarizeException(ex));
        }
    }

    private async Task ApplySelectedAsync()
    {
        SyncSelection();
        var candidates = _jobs
            .Where(job => job.Selected && job.State is PackageState.Ready or PackageState.Warning)
            .ToArray();

        if (candidates.Length == 0)
        {
            MessageBox.Show(
                "No checked package is ready to apply. Fix errors or recheck first.",
                "rpack packages",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var warningCount = candidates.Count(job => job.State == PackageState.Warning);
        var dirtyCount = candidates.Count(job => job.AllowDirty);
        var strictContextCount = candidates.Count(job => !job.IgnoreSpaceChange);
        var answer = MessageBox.Show(
            $"Apply {candidates.Length} package(s)?{Environment.NewLine}{Environment.NewLine}Warnings: {warningCount}{Environment.NewLine}Dirty-tree mode: {dirtyCount}{Environment.NewLine}Strict context mode: {strictContextCount}",
            "Apply selected rpack packages",
            MessageBoxButtons.YesNo,
            warningCount > 0 || dirtyCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        SetButtons(enabled: false);
        try
        {
            foreach (var job in candidates)
            {
                job.State = PackageState.Applying;
                job.Message = "Applying package...";
                RefreshList();
                await Task.Run(() => ApplyJob(job));
                RefreshList();
                if (job.State == PackageState.Error)
                {
                    _list.Items.Cast<ListViewItem>().FirstOrDefault(item => ReferenceEquals(item.Tag, job))?.EnsureVisible();
                    MessageBox.Show(
                        $"Batch stopped at {Path.GetFileName(job.PackagePath)}.{Environment.NewLine}{Environment.NewLine}{job.Message}",
                        "rpack apply failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    break;
                }
            }
        }
        finally
        {
            SetButtons(enabled: true);
            RefreshList();
        }
    }

    private void ApplyJob(PackageJob job)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(job.RepositoryPath))
            {
                job.Fail(
                    "Apply",
                    new PackageProblem("Repository missing", "The package has no resolved target repository.", "Recheck the package.", "RepositoryPath is empty."));
                return;
            }

            var apply = _service.Apply(new ApplyPackageOptions
            {
                PackagePath = job.PackagePath,
                RepositoryPath = job.RepositoryPath,
                AllowDirty = job.AllowDirty,
                StrictBase = job.StrictBase,
                PathPrefix = job.PathPrefix,
                AllowedDirtyPaths = GetPackageDirtyException(job.RepositoryPath, job.PackagePath),
                IgnoreSpaceChange = job.IgnoreSpaceChange
            });

            if (!apply.Success)
            {
                job.Fail("Apply", ErrorFormatter.FromCheckFailure(apply.Message, job.AllowDirty, job.IgnoreSpaceChange));
                return;
            }

            job.State = PackageState.Applied;
            job.Message = apply.Message;
            job.Details = BuildSuccessDetails(job, "Apply", $"{apply.Message}{Environment.NewLine}{Environment.NewLine}Review the working tree before committing. Rollback: rpack undo");
        }
        catch (Exception ex)
        {
            job.Fail("Apply", ErrorFormatter.SummarizeException(ex));
        }
    }

    private string? ResolveRepositoryPath(PackageJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.RepositoryOption))
        {
            return _gitClient.InspectRepository(job.RepositoryOption).RootPath;
        }

        var packageRepositoryHint = TryResolveProjectPathFromPackage(job.PackagePath);
        if (!string.IsNullOrWhiteSpace(packageRepositoryHint))
        {
            return packageRepositoryHint;
        }

        return _gitClient.FindRepositoryFrom(job.PackagePath)?.RootPath;
    }

    private string? TryResolveProjectPathFromPackage(string packagePath)
    {
        try
        {
            var inspection = _service.Inspect(packagePath);
            var projectPath = inspection.Manifest.Source?.ProjectPath;
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return null;
            }

            return _gitClient.InspectRepository(projectPath).RootPath;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetPackageDirtyException(string repositoryPath, string packagePath)
    {
        var repositoryRoot = Path.GetFullPath(repositoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPackagePath = Path.GetFullPath(packagePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var repositoryPrefix = repositoryRoot + Path.DirectorySeparatorChar;
        if (!fullPackagePath.StartsWith(repositoryPrefix, comparison))
        {
            return [];
        }

        var relativePath = Path.GetRelativePath(repositoryRoot, fullPackagePath).Replace('\\', '/');
        return relativePath.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relativePath)
            ? []
            : [relativePath];
    }

    private void RemoveSelected()
    {
        SyncSelection();
        _jobs.RemoveAll(job => job.Selected);
        RefreshList();
    }

    private void SyncSelection()
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is PackageJob job)
            {
                job.Selected = item.Checked;
            }
        }
    }

    private void RefreshList()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RefreshList);
            return;
        }

        _list.BeginUpdate();
        try
        {
            var selectedPath = _list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is PackageJob selected
                ? selected.PackagePath
                : "";
            _list.Items.Clear();
            foreach (var job in _jobs)
            {
                var item = new ListViewItem(StateText(job.State))
                {
                    Tag = job,
                    Checked = job.Selected,
                    ForeColor = StateColor(job.State)
                };
                item.SubItems.Add(Path.GetFileName(job.PackagePath));
                item.SubItems.Add(job.Inspection?.Manifest.Title ?? "");
                item.SubItems.Add(ShortenPath(job.RepositoryPath ?? job.RepositoryOption ?? ""));
                item.SubItems.Add(job.Inspection?.Manifest.Patches.Count.ToString() ?? "");
                item.SubItems.Add(job.Inspection?.DiffStats.FileCount.ToString() ?? "");
                item.SubItems.Add(job.Inspection is null ? "" : $"+{job.Inspection.DiffStats.AddedLines} -{job.Inspection.DiffStats.RemovedLines}");
                item.SubItems.Add(job.Inspection?.DiffStats.HunkCount.ToString() ?? "");
                item.SubItems.Add(BuildModeText(job));
                item.SubItems.Add(job.Message);
                _list.Items.Add(item);
                if (string.Equals(selectedPath, job.PackagePath, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                }
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        var ready = _jobs.Count(job => job.State is PackageState.Ready or PackageState.Warning);
        var errors = _jobs.Count(job => job.State == PackageState.Error);
        var applied = _jobs.Count(job => job.State == PackageState.Applied);
        _summary.Text = $"{_jobs.Count} package(s) loaded. Ready: {ready}. Errors: {errors}. Applied: {applied}.";
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (_list.SelectedItems.Count == 0)
        {
            _details.Text = "";
            return;
        }

        _details.Text = _list.SelectedItems[0].Tag is PackageJob job
            ? job.Details
            : "";
    }

    private void AppendProcessLog(ProcessLogEntry entry)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired || !IsHandleCreated)
        {
            try
            {
                BeginInvoke(() => AppendProcessLog(entry));
            }
            catch (InvalidOperationException)
            {
                return;
            }

            return;
        }

        if (_consoleLog.TextLength > 0)
        {
            _consoleLog.AppendText(Environment.NewLine);
        }

        _consoleLog.AppendText(FormatProcessLog(entry));
        _consoleLog.AppendText(Environment.NewLine);
    }

    private static string FormatProcessLog(ProcessLogEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{entry.Timestamp:HH:mm:ss}] {FormatCommand(entry.FileName, entry.Arguments)}");
        builder.AppendLine($"cwd: {entry.WorkingDirectory}");
        builder.AppendLine($"exit: {entry.ExitCode}");

        if (!string.IsNullOrWhiteSpace(entry.StandardOutput))
        {
            builder.AppendLine("stdout:");
            builder.AppendLine(entry.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(entry.StandardError))
        {
            builder.AppendLine("stderr:");
            builder.AppendLine(entry.StandardError.TrimEnd());
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatCommand(string fileName, IReadOnlyList<string> arguments)
    {
        return string.Join(" ", new[] { fileName }.Concat(arguments).Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace) || argument.Contains('"')
            ? $"\"{argument.Replace("\"", "\\\"")}\""
            : argument;
    }

    private static string BuildSuccessDetails(PackageJob job, string stage, string message)
    {
        var inspection = job.Inspection;
        if (inspection is null)
        {
            return "";
        }

        var patchSummaries = inspection.DiffStats.Patches
            .Select(
                patch =>
                    $"{patch.Title}{Environment.NewLine}" +
                    string.Join(
                        Environment.NewLine,
                        patch.Files.Select(file => $"  {file.Status,-8} +{file.AddedLines,-5} -{file.RemovedLines,-5} h:{file.HunkCount,-3} {file.Category,-8} {file.Path}")) +
                    $"{Environment.NewLine}  Subtotal: +{patch.AddedLines} -{patch.RemovedLines} hunks:{patch.HunkCount}");

        var patchText = patchSummaries.Any()
            ? string.Join(Environment.NewLine + Environment.NewLine, patchSummaries)
            : "No patch changes detected.";

        var changedFiles = inspection.ChangedFiles
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(file => $"  {file.Status,-8} +{file.AddedLines,-5} -{file.RemovedLines,-5} h:{file.HunkCount,-3} {file.Category,-8} {file.Path}");
        var limited = changedFiles.Take(120).ToArray();
        var more = inspection.ChangedFiles.Count > 120
            ? $"{Environment.NewLine}  ... {inspection.ChangedFiles.Count - 120} more file(s)"
            : "";

        return $"""
            Stage: {stage}
            Package: {job.PackagePath}
            Repository: {job.RepositoryPath}
            Mode: {(job.AllowDirty ? $"Dirty allowed ({job.DirtyReason})" : "Clean required")}
            Whitespace context: {(job.IgnoreSpaceChange ? "whitespace-compatible" : "strict")}
            Path prefix: {(string.IsNullOrWhiteSpace(job.PathPrefix) ? "(none)" : job.PathPrefix)}
            Strict base: {job.StrictBase}

            Result:
            {message.Trim()}

            Package summary:
            - patches: {inspection.DiffStats.PatchCount}
            - files changed: {inspection.DiffStats.FileCount}
            - lines added: {inspection.DiffStats.AddedLines}
            - lines removed: {inspection.DiffStats.RemovedLines}
            - total diff hunks: {inspection.DiffStats.HunkCount}
            - binary files: {inspection.DiffStats.BinaryFileCount}

            Patches:
            {patchText}

            Files:
            {string.Join(Environment.NewLine, limited)}{more}
            """;
    }

    private static string BuildErrorDetails(PackageJob job, string stage, PackageProblem problem)
    {
        return $"""
            Stage: {stage}
            Package: {job.PackagePath}
            Repository: {job.RepositoryPath ?? job.RepositoryOption ?? "(not resolved)"}
            Mode: {(job.AllowDirty ? $"Dirty allowed ({job.DirtyReason})" : "Clean required")}
            Whitespace context: {(job.IgnoreSpaceChange ? "whitespace-compatible" : "strict")}
            Path prefix: {(string.IsNullOrWhiteSpace(job.PathPrefix) ? "(none)" : job.PathPrefix)}
            Strict base: {job.StrictBase}

            Problem:
            {problem.Title}

            Summary:
            {problem.Summary}

            Suggested action:
            {problem.Suggestion}

            Raw details:
            {problem.Details}
            """;
    }

    private void SetButtons(bool enabled)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetButtons(enabled));
            return;
        }

        _applyButton.Enabled = enabled;
        _recheckButton.Enabled = enabled;
        _removeButton.Enabled = enabled;
        _copyButton.Enabled = enabled;
        _ignoreSpaceChangeBox.Enabled = enabled;
    }

    private static string StateText(PackageState state)
    {
        return state switch
        {
            PackageState.Pending => "Pending",
            PackageState.Checking => "Checking",
            PackageState.Ready => "Ready",
            PackageState.Warning => "Warning",
            PackageState.Applying => "Applying",
            PackageState.Applied => "Applied",
            PackageState.Error => "Error",
            _ => state.ToString()
        };
    }

    private static Color StateColor(PackageState state)
    {
        return state switch
        {
            PackageState.Ready => Color.DarkGreen,
            PackageState.Warning => Color.DarkGoldenrod,
            PackageState.Error => Color.DarkRed,
            PackageState.Applied => Color.DarkBlue,
            PackageState.Applying or PackageState.Checking => Color.DimGray,
            _ => SystemColors.WindowText
        };
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length <= 42)
        {
            return path;
        }

        return $"...{path[^39..]}";
    }

    private static string BuildModeText(PackageJob job)
    {
        var parts = new List<string>
        {
            job.AllowDirty ? "Dirty allowed" : "Clean required"
        };
        parts.Add(job.IgnoreSpaceChange ? "Whitespace compatible" : "Strict context");

        return string.Join(", ", parts);
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
    }

    private sealed class PackageJob
    {
        public string PackagePath { get; init; } = "";
        public string? RepositoryOption { get; set; }
        public string? RepositoryPath { get; set; }
        public string? PathPrefix { get; set; }
        public bool AllowDirty { get; set; }
        public bool StrictBase { get; set; }
        public bool IgnoreSpaceChange { get; set; }
        public string DirtyReason { get; set; } = "";
        public bool Selected { get; set; } = true;
        public PackageState State { get; set; } = PackageState.Pending;
        public PackageInspection? Inspection { get; set; }
        public string Message { get; set; } = "Waiting for validation.";
        public string Details { get; set; } = "";

        public void ResetForCheck()
        {
            State = PackageState.Pending;
            Message = "Waiting for validation.";
            Details = "";
            Inspection = null;
        }

        public void Fail(string stage, PackageProblem problem)
        {
            State = PackageState.Error;
            Message = problem.Title;
            Details = BuildErrorDetails(this, stage, problem);
        }
    }

    private enum PackageState
    {
        Pending,
        Checking,
        Ready,
        Warning,
        Applying,
        Applied,
        Error
    }
}
