using System.Globalization;
using Microsoft.Xna.Framework;

namespace MapleClaude.Debug;

/// <summary>
/// WinForms overlay window that lists every <see cref="DebugItem"/>
/// registered with the <see cref="DebugRegistry"/> and lets the user edit
/// each item's X/Y values live. Below the item table is a streaming log
/// view fed by <see cref="DebugLogSink"/>. Launch this on a dedicated STA
/// thread; don't share it with the MonoGame main thread.
/// </summary>
public sealed class DebugWindow : Form
{
    private readonly DebugRegistry _registry;
    private readonly DebugLogSink _logSink;
    private readonly DataGridView _grid;
    private readonly TextBox _log;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private bool _suppressEdit;

    private const int GridColumnCategory = 0;
    private const int GridColumnName = 1;
    private const int GridColumnX = 2;
    private const int GridColumnY = 3;

    public DebugWindow(DebugRegistry registry, DebugLogSink logSink)
    {
        _registry = registry;
        _logSink = logSink;

        Text = "MapleClaude Debug";
        Width = 700;
        Height = 700;
        StartPosition = FormStartPosition.Manual;
        Location = new System.Drawing.Point(20, 20);
        ShowInTaskbar = true;

        // Top panel: drag-mode toggle. Added BEFORE the SplitContainer so the
        // Top dock anchors it and the Fill takes the remaining height.
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
        };
        var dragCheckbox = new CheckBox
        {
            Text = "Drag mode (click + drag tunables in the game window)",
            AutoSize = true,
            Location = new System.Drawing.Point(8, 6),
            Checked = _registry.DragMode,
        };
        dragCheckbox.CheckedChanged += (_, _) => _registry.DragMode = dragCheckbox.Checked;
        topPanel.Controls.Add(dragCheckbox);

        var copyButton = new System.Windows.Forms.Button
        {
            Text = "Copy table",
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        // Place near the right edge; recompute on resize.
        void positionCopy()
        {
            copyButton.Location = new System.Drawing.Point(topPanel.ClientSize.Width - copyButton.Width - 8, 3);
        }
        topPanel.Resize += (_, _) => positionCopy();
        copyButton.Click += (_, _) => CopyTableToClipboard();
        topPanel.Controls.Add(copyButton);
        positionCopy();

        Controls.Add(topPanel);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 380,
            FixedPanel = FixedPanel.None,
        };
        Controls.Add(split);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            BackgroundColor = System.Drawing.Color.White,
            BorderStyle = BorderStyle.None,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Category", ReadOnly = true, FillWeight = 25 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", ReadOnly = true, FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "X", ReadOnly = false, FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Y", ReadOnly = false, FillWeight = 20 });
        _grid.CellEndEdit += OnCellEndEdit;
        split.Panel1.Controls.Add(_grid);

        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 9f),
            BackColor = System.Drawing.Color.Black,
            ForeColor = System.Drawing.Color.LightGray,
        };
        split.Panel2.Controls.Add(_log);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _refreshTimer.Tick += (_, _) => Tick();
        _refreshTimer.Start();

        _registry.ItemsChanged += () => BeginInvoke(new Action(RebuildGrid));
        RebuildGrid();
    }

    private void RebuildGrid()
    {
        _suppressEdit = true;
        try
        {
            _grid.Rows.Clear();
            foreach (var item in _registry.Snapshot()
                         .OrderBy(i => i.Category, StringComparer.Ordinal)
                         .ThenBy(i => i.Name, StringComparer.Ordinal))
            {
                var v = item.Get();
                _grid.Rows.Add(item.Category, item.Name,
                    v.X.ToString("0.##", CultureInfo.InvariantCulture),
                    v.Y.ToString("0.##", CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            _suppressEdit = false;
        }
    }

    private void Tick()
    {
        // Refresh values from the live registry (in case the game mutates them
        // independently of debug edits) — but only if the user isn't editing.
        if (!_grid.IsCurrentCellInEditMode)
        {
            _suppressEdit = true;
            try
            {
                var snapshot = _registry.Snapshot()
                    .OrderBy(i => i.Category, StringComparer.Ordinal)
                    .ThenBy(i => i.Name, StringComparer.Ordinal)
                    .ToList();
                if (snapshot.Count == _grid.Rows.Count)
                {
                    for (var i = 0; i < snapshot.Count; i++)
                    {
                        var v = snapshot[i].Get();
                        var row = _grid.Rows[i];
                        var xStr = v.X.ToString("0.##", CultureInfo.InvariantCulture);
                        var yStr = v.Y.ToString("0.##", CultureInfo.InvariantCulture);
                        if (!Equals(row.Cells[GridColumnX].Value, xStr))
                        {
                            row.Cells[GridColumnX].Value = xStr;
                        }
                        if (!Equals(row.Cells[GridColumnY].Value, yStr))
                        {
                            row.Cells[GridColumnY].Value = yStr;
                        }
                    }
                }
                else
                {
                    RebuildGrid();
                }
            }
            finally
            {
                _suppressEdit = false;
            }
        }

        // Drain log queue → TextBox.
        var lines = _logSink.Drain();
        if (lines.Count > 0)
        {
            _log.AppendText(string.Join(Environment.NewLine, lines) + Environment.NewLine);
            // Trim if the buffer gets huge.
            if (_log.TextLength > 200_000)
            {
                _log.Text = _log.Text[^120_000..];
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }
        }
    }

    private void OnCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressEdit)
        {
            return;
        }
        if (e.ColumnIndex != GridColumnX && e.ColumnIndex != GridColumnY)
        {
            return;
        }

        var row = _grid.Rows[e.RowIndex];
        var category = row.Cells[GridColumnCategory].Value?.ToString() ?? "";
        var name = row.Cells[GridColumnName].Value?.ToString() ?? "";
        var item = _registry.Snapshot()
            .FirstOrDefault(i => i.Category == category && i.Name == name);
        if (item is null)
        {
            return;
        }

        var current = item.Get();
        var xText = row.Cells[GridColumnX].Value?.ToString() ?? "";
        var yText = row.Cells[GridColumnY].Value?.ToString() ?? "";
        if (!float.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
        {
            x = current.X;
        }
        if (!float.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            y = current.Y;
        }
        item.Set(new Vector2(x, y));
    }

    private void CopyTableToClipboard()
    {
        var sb = new System.Text.StringBuilder();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            sb.Append(row.Cells[GridColumnCategory].Value);
            sb.Append('\t');
            sb.Append(row.Cells[GridColumnName].Value);
            sb.Append('\t');
            sb.Append(row.Cells[GridColumnX].Value);
            sb.Append('\t');
            sb.Append(row.Cells[GridColumnY].Value);
            sb.AppendLine();
        }
        if (sb.Length == 0)
        {
            return;
        }
        try
        {
            Clipboard.SetText(sb.ToString());
        }
        catch
        {
            // Clipboard.SetText can race with other apps holding it open — quietly ignore.
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        base.OnFormClosed(e);
    }
}
