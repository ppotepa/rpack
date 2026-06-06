using System.Drawing;
using System.Windows.Forms;
using Rpack.Core;

namespace Rpack.Open;

internal sealed class OpenBatchForm : Form
{
    private readonly GitClient _gitClient = new(new ProcessRunner());
    private readonly RpackPackageService _service;
    private readonly List<PackageJob> _jobs = [];
    private readonly ListView _list = new();
    private readonly TextBox _details = new();
    private readonly Label _summary = new();
    private readonly Button _recheckButton = new();
    private readonly Button _applyButton = new();
    private readonly Button _removeButton = new();
    private readonly Button _copyButton = new();
    private readonly System.Windows.Forms.Timer _checkTimer = new();
    private bool _checking;

    public OpenBatchForm()
    {
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
        if (InvokeRequired)
        {
            BeginInvoke(() => AddRequest(request));
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
                StrictBase = request.StrictBase
            });
        }

        RefreshList();
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
        _list.Columns.Add("Lines", 80);
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
            if (!string.IsNullOrWhiteSpace(_details.Text))
            {
                Clipboard.SetText(_details.Text);
            }
        };

        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(_applyButton);
        buttons.Controls.Add(_recheckButton);
        buttons.Controls.Add(_removeButton);
        buttons.Controls.Add(_copyButton);

        root.Controls.Add(_summary, 0, 0);
        root.Controls.Add(_list, 0, 1);
        root.Controls.Add(_details, 0, 2);
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
                AllowedDirtyPaths = allowedDirtyPaths
            });

            job.Inspection = inspection;
            if (!check.Success)
            {
                job.Fail("Check", ErrorFormatter.FromCheckFailure(check.Message, job.AllowDirty));
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
        var answer = MessageBox.Show(
            $"Apply {candidates.Length} package(s)?{Environment.NewLine}{Environment.NewLine}Warnings: {warningCount}{Environment.NewLine}Dirty-tree mode: {dirtyCount}",
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
                AllowedDirtyPaths = GetPackageDirtyException(job.RepositoryPath, job.PackagePath)
            });

            if (!apply.Success)
            {
                job.Fail("Apply", ErrorFormatter.FromCheckFailure(apply.Message, job.AllowDirty));
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

        return _gitClient.FindRepositoryFrom(job.PackagePath)?.RootPath;
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
                item.SubItems.Add(job.Inspection?.ChangedFiles.Count.ToString() ?? "");
                item.SubItems.Add(job.Inspection is null ? "" : $"+{job.Inspection.AddedLines} -{job.Inspection.RemovedLines}");
                item.SubItems.Add(job.AllowDirty ? "Dirty allowed" : "Clean required");
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

    private static string BuildSuccessDetails(PackageJob job, string stage, string message)
    {
        var inspection = job.Inspection;
        var changedFiles = inspection?.ChangedFiles.Take(80)
            .Select(file => $"  {file.Status,-8} +{file.AddedLines,-4} -{file.RemovedLines,-4} {file.Path}")
            .ToArray() ?? [];
        var more = inspection is not null && inspection.ChangedFiles.Count > changedFiles.Length
            ? $"{Environment.NewLine}  ... {inspection.ChangedFiles.Count - changedFiles.Length} more file(s)"
            : "";

        return $"""
            Stage: {stage}
            Package: {job.PackagePath}
            Repository: {job.RepositoryPath}
            Mode: {(job.AllowDirty ? $"Dirty allowed ({job.DirtyReason})" : "Clean required")}
            Path prefix: {(string.IsNullOrWhiteSpace(job.PathPrefix) ? "(none)" : job.PathPrefix)}
            Strict base: {job.StrictBase}

            Result:
            {message.Trim()}

            Manifest:
            Id: {inspection?.Manifest.Id}
            Title: {inspection?.Manifest.Title}
            Patches: {inspection?.Manifest.Patches.Count}
            Changed files: {inspection?.ChangedFiles.Count}
            Lines: +{inspection?.AddedLines} -{inspection?.RemovedLines}

            Files:
            {string.Join(Environment.NewLine, changedFiles)}{more}
            """;
    }

    private static string BuildErrorDetails(PackageJob job, string stage, PackageProblem problem)
    {
        return $"""
            Stage: {stage}
            Package: {job.PackagePath}
            Repository: {job.RepositoryPath ?? job.RepositoryOption ?? "(not resolved)"}
            Mode: {(job.AllowDirty ? $"Dirty allowed ({job.DirtyReason})" : "Clean required")}
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
