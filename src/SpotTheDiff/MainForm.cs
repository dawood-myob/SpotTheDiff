using System.Drawing.Imaging;
using SpotTheDiff.Core;

namespace SpotTheDiff;

internal sealed class MainForm : Form
{
    private enum Slot { Auto, Before, After }

    private readonly ImageViewer _viewBefore = new() { Dock = DockStyle.Fill, Placeholder = "BEFORE / LEFT\r\n\r\nCtrl+V to paste, drop a file, or right-click" };
    private readonly ImageViewer _viewAfter = new() { Dock = DockStyle.Fill, Placeholder = "AFTER / RIGHT\r\n\r\nCtrl+V to paste, drop a file, or right-click" };
    private readonly ImageViewer _viewResult = new() { Dock = DockStyle.Fill, Placeholder = "Differences will be shown here" };

    private readonly CheckBox _single = new() { Text = "One image with both screens side by side (split in half)", Checked = true, AutoSize = true };
    private readonly TrackBar _tolerance = new() { Minimum = 0, Maximum = 100, Value = 25, TickFrequency = 10, Width = 140, AutoSize = false, Height = 26 };
    private readonly Label _toleranceValue = new() { AutoSize = true, Text = "25" };
    private readonly NumericUpDown _merge = new() { Minimum = 0, Maximum = 200, Value = 12, Width = 55 };
    private readonly NumericUpDown _minSize = new() { Minimum = 1, Maximum = 100000, Value = 12, Width = 65 };
    private readonly ComboBox _shape = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ComboBox _layout = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly CheckBox _tint = new() { Text = "Tint changed pixels", AutoSize = true };
    private readonly ToolStripStatusLabel _status = new() { Text = "Take a snip (Win+Shift+S) and press Ctrl+V." };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };

    private Bitmap? _before;
    private Bitmap? _after;
    private DiffResult? _result;

    public MainForm()
    {
        Text = "Spot The Diff";
        Size = new Size(1500, 950);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        _shape.Items.AddRange(new object[] { "Rectangle", "Circle" });
        _shape.SelectedIndex = 0;
        _layout.Items.AddRange(new object[] { "Side by side (before | after)", "After image only" });
        _layout.SelectedIndex = 0;

        BuildLayout();
        WireEvents();
    }

    private void BuildLayout()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 4, 6, 4),
            WrapContents = true
        };

        toolbar.Controls.Add(MakeButton("Paste  (Ctrl+V)", (_, _) => PasteAuto(), bold: true));
        toolbar.Controls.Add(MakeButton("Open...", (_, _) => OpenInto(Slot.Auto)));
        toolbar.Controls.Add(MakeButton("Swap", (_, _) => Swap()));
        toolbar.Controls.Add(MakeButton("Clear", (_, _) => ClearAll()));
        toolbar.Controls.Add(Spacer());
        _single.Margin = new Padding(3, 8, 12, 0);
        toolbar.Controls.Add(_single);
        toolbar.SetFlowBreak(_single, true);

        toolbar.Controls.Add(Caption("Tolerance"));
        _tolerance.Margin = new Padding(0, 2, 0, 0);
        toolbar.Controls.Add(_tolerance);
        _toleranceValue.Margin = new Padding(0, 8, 12, 0);
        toolbar.Controls.Add(_toleranceValue);
        toolbar.Controls.Add(Caption("Merge nearby (px)"));
        toolbar.Controls.Add(_merge);
        toolbar.Controls.Add(Caption("Ignore smaller than (px)"));
        toolbar.Controls.Add(_minSize);
        toolbar.Controls.Add(Caption("Mark with"));
        toolbar.Controls.Add(_shape);
        toolbar.Controls.Add(Caption("Output"));
        toolbar.Controls.Add(_layout);
        _tint.Margin = new Padding(12, 7, 3, 0);
        toolbar.Controls.Add(_tint);

        var inputs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        inputs.Controls.Add(Wrap("Before (left)  -  wheel = zoom, drag = pan, double-click = fit", _viewBefore), 0, 0);
        inputs.Controls.Add(Wrap("After (right)", _viewAfter), 1, 0);

        var resultBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(2) };
        resultBar.Controls.Add(MakeButton("Copy result", (_, _) => CopyResult()));
        resultBar.Controls.Add(MakeButton("Save result...  (Ctrl+S)", (_, _) => SaveResult()));
        resultBar.Controls.Add(MakeButton("Fit", (_, _) => _viewResult.FitToWindow()));

        var resultBox = Wrap("Differences", _viewResult);
        resultBox.Controls.Add(resultBar);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(inputs);
        split.Panel2.Controls.Add(resultBox);
        Load += (_, _) => split.SplitterDistance = (int)(split.Height * 0.38);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        // Add order matters for docking: fill first, then edges.
        Controls.Add(split);
        Controls.Add(toolbar);
        Controls.Add(statusStrip);

        _viewBefore.ContextMenuStrip = MakeContextMenu(Slot.Before);
        _viewAfter.ContextMenuStrip = MakeContextMenu(Slot.After);
    }

    private void WireEvents()
    {
        // Keep the two input views panned/zoomed together.
        bool syncing = false;
        _viewBefore.ViewChanged += (_, _) => Sync(_viewBefore, _viewAfter);
        _viewAfter.ViewChanged += (_, _) => Sync(_viewAfter, _viewBefore);
        void Sync(ImageViewer from, ImageViewer to)
        {
            if (syncing) return;
            syncing = true;
            to.ApplyState(from.State);
            syncing = false;
        }

        _viewBefore.FileDropped += (_, path) => LoadFileInto(path, Slot.Before);
        _viewAfter.FileDropped += (_, path) => LoadFileInto(path, Slot.After);
        _viewResult.FileDropped += (_, path) => LoadFileInto(path, Slot.Auto);

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RunCompare();
        };

        _tolerance.ValueChanged += (_, _) =>
        {
            _toleranceValue.Text = _tolerance.Value.ToString();
            ScheduleCompare();
        };
        _merge.ValueChanged += (_, _) => ScheduleCompare();
        _minSize.ValueChanged += (_, _) => ScheduleCompare();
        _shape.SelectedIndexChanged += (_, _) => ScheduleCompare();
        _layout.SelectedIndexChanged += (_, _) => ScheduleCompare();
        _tint.CheckedChanged += (_, _) => ScheduleCompare();

        FormClosed += (_, _) =>
        {
            _before?.Dispose();
            _after?.Dispose();
            _result?.Dispose();
        };
    }

    // ---------- UI helpers ----------

    private static Button MakeButton(string text, EventHandler onClick, bool bold = false)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(3) };
        if (bold) b.Font = new Font(b.Font, FontStyle.Bold);
        b.Click += onClick;
        return b;
    }

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(6, 8, 2, 0) };

    private static Control Spacer() => new Panel { Width = 20, Height = 1, Margin = new Padding(0) };

    private static GroupBox Wrap(string title, Control inner)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 6) };
        box.Controls.Add(inner);
        return box;
    }

    private ContextMenuStrip MakeContextMenu(Slot slot)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Paste here", null, (_, _) => PasteInto(slot));
        menu.Items.Add("Open...", null, (_, _) => OpenInto(slot));
        menu.Items.Add("Clear", null, (_, _) => ClearSlot(slot));
        return menu;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool inTextBox = ActiveControl is TextBoxBase or NumericUpDown;
        switch (keyData)
        {
            case Keys.Control | Keys.V when !inTextBox || ImageUtil.ClipboardHasImage():
                PasteAuto();
                return true;
            case Keys.Control | Keys.C when !inTextBox && _result != null:
                CopyResult();
                return true;
            case Keys.Control | Keys.S:
                SaveResult();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------- Loading images ----------

    private void PasteAuto() => PasteInto(Slot.Auto);

    private void PasteInto(Slot slot)
    {
        Bitmap? img = ImageUtil.TryGetClipboardImage();
        if (img == null)
        {
            SetStatus("The clipboard does not contain an image. Take a snip with Win+Shift+S first.");
            return;
        }
        Accept(img, slot);
    }

    private void OpenInto(Slot slot)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            LoadFileInto(dlg.FileName, slot);
    }

    private void LoadFileInto(string path, Slot slot)
    {
        try
        {
            Accept(ImageUtil.LoadFile(path), slot);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException or UnauthorizedAccessException)
        {
            SetStatus($"Could not open '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    /// <summary>Takes ownership of <paramref name="img"/>.</summary>
    private void Accept(Bitmap img, Slot slot)
    {
        if (_single.Checked && slot == Slot.Auto)
        {
            SplitIntoHalves(img);
        }
        else
        {
            if (slot == Slot.Auto)
            {
                if (_before == null) slot = Slot.Before;
                else if (_after == null) slot = Slot.After;
                else
                {
                    // Both slots full: start a new comparison.
                    SetBefore(null);
                    SetAfter(null);
                    slot = Slot.Before;
                }
            }

            if (slot == Slot.Before) SetBefore(img); else SetAfter(img);
        }

        _debounce.Stop();
        RunCompare();
    }

    private void SplitIntoHalves(Bitmap source)
    {
        using (source)
        {
            int half = source.Width / 2;
            if (half < 1)
            {
                SetStatus("The image is too narrow to split.");
                return;
            }
            var rect = new Rectangle(0, 0, half, source.Height);
            SetBefore(source.Clone(rect, PixelFormat.Format32bppArgb));
            SetAfter(source.Clone(new Rectangle(source.Width - half, 0, half, source.Height), PixelFormat.Format32bppArgb));
        }
    }

    private void SetBefore(Bitmap? bmp)
    {
        Bitmap? old = _before;
        _before = bmp;
        _viewBefore.Image = bmp;
        old?.Dispose();
    }

    private void SetAfter(Bitmap? bmp)
    {
        Bitmap? old = _after;
        _after = bmp;
        _viewAfter.Image = bmp;
        old?.Dispose();
    }

    private void SetResult(DiffResult? result)
    {
        DiffResult? old = _result;
        _result = result;
        _viewResult.Image = result?.Annotated;
        old?.Dispose();
    }

    private void ClearSlot(Slot slot)
    {
        if (slot == Slot.Before) SetBefore(null); else SetAfter(null);
        RunCompare();
    }

    private void ClearAll()
    {
        SetBefore(null);
        SetAfter(null);
        SetResult(null);
        SetStatus("Cleared. Take a snip (Win+Shift+S) and press Ctrl+V.");
    }

    private void Swap()
    {
        (_before, _after) = (_after, _before);
        _viewBefore.Image = _before;
        _viewAfter.Image = _after;
        RunCompare();
    }

    // ---------- Comparing ----------

    private void ScheduleCompare()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void RunCompare()
    {
        if (_before == null || _after == null)
        {
            SetResult(null);
            SetStatus(_before == null && _after == null
                ? "Take a snip (Win+Shift+S) and press Ctrl+V."
                : "Now paste the second image (Ctrl+V).");
            return;
        }

        var options = new DiffOptions
        {
            Tolerance = _tolerance.Value,
            MergeDistance = (int)_merge.Value,
            MinPixels = (int)_minSize.Value,
            Shape = _shape.SelectedIndex == 1 ? HighlightShape.Circle : HighlightShape.Rectangle,
            Layout = _layout.SelectedIndex == 1 ? OutputLayout.AfterOnly : OutputLayout.SideBySide,
            TintChangedPixels = _tint.Checked
        };

        Cursor = Cursors.WaitCursor;
        try
        {
            DiffResult result = DiffEngine.Compare(_before, _after, options);
            SetResult(result);

            int n = result.Regions.Count;
            string text = n == 0
                ? "No differences found. (Try lowering the tolerance.)"
                : $"{n} difference{(n == 1 ? "" : "s")} found, numbered top to bottom.";
            if (result.AfterWasResized) text += "  Note: the images had different sizes; 'after' was scaled to match.";
            SetStatus(text);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            SetStatus("Comparison failed: " + ex.Message);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // ---------- Output ----------

    private void CopyResult()
    {
        if (_result == null)
        {
            SetStatus("Nothing to copy yet.");
            return;
        }
        try
        {
            Clipboard.SetImage(_result.Annotated);
            SetStatus("Result copied to the clipboard - paste it into Teams, email, a ticket, etc.");
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            SetStatus("Could not copy to the clipboard: " + ex.Message);
        }
    }

    private void SaveResult()
    {
        if (_result == null)
        {
            SetStatus("Nothing to save yet.");
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Save result",
            Filter = "PNG image|*.png|JPEG image|*.jpg",
            FileName = $"differences-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            ImageFormat format = dlg.FilterIndex == 2 ? ImageFormat.Jpeg : ImageFormat.Png;
            _result.Annotated.Save(dlg.FileName, format);
            SetStatus("Saved " + dlg.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            SetStatus("Could not save: " + ex.Message);
        }
    }

    private void SetStatus(string text) => _status.Text = text;
}
