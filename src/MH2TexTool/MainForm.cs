using System.Diagnostics;
using System.Text;
using MH2TexTool.Core;

namespace MH2TexTool;

public sealed class MainForm : Form
{
    private readonly ListView _list = new();
    private readonly PictureBox _preview = new();
    private readonly Label _info = new();
    private readonly TextBox _log = new();
    private readonly ToolStripStatusLabel _status = new();
    private readonly ToolStripProgressBar _progress = new();
    private readonly ToolStrip _toolbar = new();
    private readonly ComboBox _exportFormat = new();
    private readonly ComboBox _container = new();

    private TexFile? _file;
    private bool _busy;
    private readonly string? _initialFile;

    public MainForm(string? initialFile = null)
    {
        _initialFile = initialFile;
        Text = "MH2 Tex Tool — Manhunt 2 texture editor";
        Width = 1280;
        Height = 800;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        BuildUi();
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        // меню
        var menu = new MenuStrip();

        var fileMenu = new ToolStripMenuItem("Файл");
        fileMenu.DropDownItems.Add(Item("Открыть .tex…", Keys.Control | Keys.O, OnOpen));
        fileMenu.DropDownItems.Add(Item("Сохранить как .tex…", Keys.Control | Keys.S, OnSave));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(Item("Выход", Keys.Alt | Keys.F4, (_, _) => Close()));

        var exportMenu = new ToolStripMenuItem("Экспорт");
        exportMenu.DropDownItems.Add(Item("Выбранные текстуры…", Keys.None, (_, _) => ExportDialog(true)));
        exportMenu.DropDownItems.Add(Item("Все текстуры…", Keys.None, (_, _) => ExportDialog(false)));

        var importMenu = new ToolStripMenuItem("Импорт");
        importMenu.DropDownItems.Add(Item("Папка → все текстуры…", Keys.None, (_, _) => ImportDialog(false)));
        importMenu.DropDownItems.Add(Item("Папка → выбранные…", Keys.None, (_, _) => ImportDialog(true)));
        importMenu.DropDownItems.Add(new ToolStripSeparator());
        importMenu.DropDownItems.Add(Item("Сбросить все замены", Keys.None, (_, _) => ResetChanges()));

        var toolsMenu = new ToolStripMenuItem("Инструменты");
        toolsMenu.DropDownItems.Add(Item("Конвертировать папку → PNG…", Keys.None, (_, _) => ConvertFolderDialog(true)));
        toolsMenu.DropDownItems.Add(Item("Конвертировать папку → BMP…", Keys.None, (_, _) => ConvertFolderDialog(false)));

        var helpMenu = new ToolStripMenuItem("Справка");
        helpMenu.DropDownItems.Add(Item("О формате .tex", Keys.None, (_, _) => ShowFormatHelp()));

        menu.Items.AddRange(new ToolStripItem[] { fileMenu, exportMenu, importMenu, toolsMenu, helpMenu });
        MainMenuStrip = menu;

        // тулбар
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.ImageScalingSize = new Size(20, 20);
        _toolbar.Padding = new Padding(4, 2, 4, 2);

        _toolbar.Items.Add(Button("Открыть", (_, _) => OpenDialog()));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("Экспорт всех", (_, _) => ExportDialog(false)));
        _toolbar.Items.Add(Button("Экспорт выбранных", (_, _) => ExportDialog(true)));
        _exportFormat.DropDownStyle = ComboBoxStyle.DropDownList;
        _exportFormat.Items.AddRange(new object[] { "PNG", "BMP" });
        _exportFormat.SelectedIndex = 0;
        _exportFormat.Width = 70;
        _toolbar.Items.Add(new ToolStripControlHost(_exportFormat) { Margin = new Padding(4, 2, 4, 2) });

        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("Импорт папки", (_, _) => ImportDialog(false)));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("Сохранить .tex", (_, _) => OnSave(null, EventArgs.Empty)));
        _container.DropDownStyle = ComboBoxStyle.DropDownList;
        _container.Items.AddRange(new object[] { "Как в оригинале", "TCDT (без сжатия)", "Z2HM (zlib)" });
        _container.SelectedIndex = 0;
        _container.Width = 150;
        _toolbar.Items.Add(new ToolStripControlHost(_container) { Margin = new Padding(4, 2, 4, 2) });

        // список текстур
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.GridLines = false;
        _list.Columns.Add("Имя", 260);
        _list.Columns.Add("Размер", 90);
        _list.Columns.Add("Формат", 75);
        _list.Columns.Add("Мипы", 55);
        _list.Columns.Add("Байт", 90);
        _list.Columns.Add("Статус", 130);
        _list.SelectedIndexChanged += (_, _) => UpdatePreview();

        // превью
        _preview.Dock = DockStyle.Fill;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = Color.FromArgb(28, 28, 30);

        _info.Dock = DockStyle.Bottom;
        _info.Height = 64;
        _info.Padding = new Padding(8, 6, 8, 6);
        _info.Font = new Font("Segoe UI", 9f);
        _info.Text = "Откройте .tex файл (или перетащите его в окно).";

        var previewPanel = new Panel { Dock = DockStyle.Fill };
        previewPanel.Controls.Add(_preview);
        previewPanel.Controls.Add(_info);

        var topSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 700,
            SplitterWidth = 6
        };
        topSplit.Panel1.Controls.Add(_list);
        topSplit.Panel2.Controls.Add(previewPanel);

        // лог
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font("Consolas", 8.75f);
        _log.BackColor = Color.FromArgb(245, 245, 245);

        var bottomSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6
        };
        bottomSplit.Panel1.Controls.Add(topSplit);
        bottomSplit.Panel2.Controls.Add(_log);

        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(bottomSplit);

        // статус
        var statusStrip = new StatusStrip();
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = "Готово";
        _progress.Visible = false;
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.Width = 140;
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(_progress);

        Controls.Add(host);
        Controls.Add(statusStrip);
        Controls.Add(_toolbar);
        Controls.Add(menu);

        Load += (_, _) =>
        {
            bottomSplit.SplitterDistance = (int)(bottomSplit.Height * 0.72);
            if (!string.IsNullOrEmpty(_initialFile) && File.Exists(_initialFile))
                OpenFile(_initialFile);
        };
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text, null, handler);
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        return item;
    }

    private static ToolStripButton Button(string text, EventHandler handler) =>
        new(text, null, handler) { DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(6, 0, 6, 0) };

    // --------------------------------------------------------------- файл

    private void OpenDialog()
    {
        if (_busy) return;
        using var dlg = new OpenFileDialog
        {
            Filter = "Manhunt 2 texture (*.tex)|*.tex|Все файлы|*.*",
            Title = "Открыть .tex"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName);
    }

    private void OnOpen(object? sender, EventArgs e) => OpenDialog();

    private void OpenFile(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            Log($"Открываю {path}");
            _file = TexFile.Load(path);
            Text = $"MH2 Tex Tool — {Path.GetFileName(path)}";

            var kinds = _file.Textures.GroupBy(t => t.FormatName)
                .Select(g => $"{g.Key}: {g.Count()}");
            Log($"Контейнер: {(_file.SourceKind == TexContainerKind.Z2hm ? "Z2HM (zlib)" : "TCDT")}, текстур: {_file.Textures.Count} ({string.Join(", ", kinds)})");

            RefreshList();
            if (_file.Textures.Count > 0) _list.Items[0].Selected = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось открыть файл", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log("ОШИБКА: " + ex.Message);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        if (_file != null)
        {
            foreach (var tex in _file.Textures)
            {
                var item = new ListViewItem(tex.Name) { Tag = tex };
                item.SubItems.Add(tex.SizeText);
                item.SubItems.Add(tex.FormatName);
                item.SubItems.Add(tex.MipCount.ToString());
                item.SubItems.Add(tex.CurrentDds.Length.ToString("N0"));
                if (tex.IsModified)
                {
                    var ni = DdsCodec.ReadInfo(tex.CurrentDds);
                    string was = $"{tex.OriginalWidth}×{tex.OriginalHeight}";
                    string now = $"{ni.Width}×{ni.Height}";
                    item.SubItems.Add(was == now ? "заменена" : $"заменена {was}→{now}");
                }
                else
                {
                    item.SubItems.Add("");
                }
                if (tex.IsModified) item.ForeColor = Color.FromArgb(0, 110, 0);
                _list.Items.Add(item);
            }
        }
        _list.EndUpdate();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_file == null)
        {
            _status.Text = "Готово";
            return;
        }
        _status.Text = $"{Path.GetFileName(_file.SourcePath)} — текстур: {_file.Textures.Count}, заменено: {_file.ModifiedCount}";
    }

    private void UpdatePreview()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not TexTexture tex)
        {
            _preview.Image = null;
            return;
        }

        try
        {
            var bmp = DdsCodec.DecodeToBitmap(tex.CurrentDds);
            var old = _preview.Image;
            _preview.Image = bmp;
            old?.Dispose();

            var info = DdsCodec.ReadInfo(tex.CurrentDds);
            _info.Text =
                $"{tex.Name}\r\n" +
                $"{info.Width}×{info.Height}, {tex.FormatName}, мипов: {tex.MipCount}, " +
                $"блок: {tex.CurrentDds.Length:N0} байт, смещение: 0x{tex.DataOffset:X}" +
                (tex.IsModified ? "   [ЗАМЕНЕНА]" : "");
        }
        catch (Exception ex)
        {
            _preview.Image = null;
            _info.Text = $"{tex.Name}: не удалось показать превью ({ex.Message})";
        }
    }

    // ------------------------------------------------------------- экспорт

    private async void ExportDialog(bool onlySelected)
    {
        if (_file == null || _busy) return;

        var targets = onlySelected
            ? _list.SelectedItems.Cast<ListViewItem>().Select(i => (TexTexture)i.Tag!).ToList()
            : _file.Textures;
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Ничего не выбрано.", "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new FolderBrowserDialog { Description = "Куда сохранить изображения" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        bool png = _exportFormat.SelectedIndex == 0;
        string folder = dlg.SelectedPath;

        await RunBusy($"Экспорт {targets.Count} текстур…", progress =>
        {
            int ok = 0, fail = 0;
            var occurrences = new Occurrences();
            foreach (var tex in targets)
            {
                try
                {
                    using var bmp = DdsCodec.DecodeToBitmap(tex.CurrentDds);
                    string name = occurrences.Next(Cli.SafeName(tex.Name));
                    var dest = Path.Combine(folder, name + (png ? ".png" : ".bmp"));
                    if (png) bmp.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
                    else ImageBatch.SaveBmp24(bmp, dest);
                    ok++;
                    progress.Report($"{tex.Name} → {Path.GetFileName(dest)} ({bmp.Width}×{bmp.Height})");
                }
                catch (Exception ex)
                {
                    fail++;
                    progress.Report($"ОШИБКА {tex.Name}: {ex.Message}");
                }
            }
            progress.Report($"Экспорт завершён: {ok} ok, {fail} ошибок → {folder}");
        });
    }

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "texture" : name;
    }

    // -------------------------------------------------------------- импорт

    private async void ImportDialog(bool onlySelected, string? folder = null)
    {
        if (_file == null || _busy) return;

        if (folder == null)
        {
            using var dlg = new FolderBrowserDialog { Description = "Папка с PNG/BMP/DDS для замены (по именам текстур)" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            folder = dlg.SelectedPath;
        }

        var targets = onlySelected
            ? _list.SelectedItems.Cast<ListViewItem>().Select(i => (TexTexture)i.Tag!).ToList()
            : _file.Textures;
        if (targets.Count == 0) return;

        string srcFolder = folder;

        await RunBusy("Импорт текстур…", progress =>
        {
            var images = TextureFolder.Scan(srcFolder);
            progress.Report($"В папке изображений: {images.Count}");

            int replaced = 0, missing = 0, failed = 0;
            var missingNames = new List<string>();
            var occurrences = new Occurrences();

            foreach (var tex in targets)
            {
                string key = occurrences.Next(Cli.SafeName(tex.Name));
                var path = images.Find(key, soft: !key.Contains('~'));
                if (path == null)
                {
                    missing++;
                    missingNames.Add(tex.Name);
                    continue;
                }

                try
                {
                    byte[] block;
                    if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                    {
                        block = File.ReadAllBytes(path);
                        if (!DdsCodec.IsDds(block)) throw new InvalidDataException("не DDS");
                    }
                    else
                    {
                        using var bmp = ImageBatch.LoadBitmap(path);
                        using var alpha = Cli.LoadAlphaSource(tex, bmp);
                        block = DdsCodec.BuildDds(tex.OriginalDds, bmp, tex.Compression, 0, alpha, Cli.MaxSize);
                        progress.Report($"{tex.Name} ← {Path.GetFileName(path)} ({bmp.Width}×{bmp.Height}, {tex.FormatName})");
                    }

                    tex.Replacement = block;
                    var info = DdsCodec.ReadInfo(block);
                    if (info.Width != tex.Width || info.Height != tex.Height)
                        progress.Report($"  {tex.Name}: размер {tex.Width}×{tex.Height} → {info.Width}×{info.Height}");
                    replaced++;
                }
                catch (Exception ex)
                {
                    failed++;
                    progress.Report($"ОШИБКА {tex.Name}: {ex.Message}");
                }
            }

            progress.Report($"Импорт: заменено {replaced}, не найдено {missing}, ошибок {failed}");
            if (missingNames.Count > 0)
            {
                progress.Report("Нет файлов для: " + string.Join(", ", missingNames.Take(25)) +
                                (missingNames.Count > 25 ? $" … (ещё {missingNames.Count - 25})" : ""));
            }
        });

        RefreshList();
        UpdatePreview();
    }

    private static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray());

    private void ResetChanges()
    {
        if (_file == null) return;
        foreach (var tex in _file.Textures) tex.Replacement = null;
        RefreshList();
        UpdatePreview();
        Log("Все замены сброшены.");
    }

    // ------------------------------------------------------------ сохранение

    private async void OnSave(object? sender, EventArgs e)
    {
        if (_file == null || _busy) return;

        using var dlg = new SaveFileDialog
        {
            Filter = "Manhunt 2 texture (*.tex)|*.tex|Все файлы|*.*",
            FileName = Path.GetFileNameWithoutExtension(_file.SourcePath) + "_new.tex",
            InitialDirectory = Path.GetDirectoryName(_file.SourcePath)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var kind = _container.SelectedIndex switch
        {
            1 => TexContainerKind.Tcdt,
            2 => TexContainerKind.Z2hm,
            _ => TexContainerKind.Preserve
        };

        string dest = dlg.FileName;
        var lines = new List<string>();

        await RunBusy("Сохранение .tex…", progress =>
        {
            var bytes = _file.Build(kind, s => lines.Add(s));
            File.WriteAllBytes(dest, bytes);
            progress.Report($"Готово: {Path.GetFileName(dest)}, {bytes.Length:N0} байт (было {new FileInfo(_file.SourcePath).Length:N0})");
        });

        foreach (var line in lines) Log(line);
        Log($"Файл записан: {dest}");
        UpdateStatus();
    }

    // --------------------------------------------------------- конвертер

    private async void ConvertFolderDialog(bool toPng)
    {
        if (_busy) return;

        using var dlg = new FolderBrowserDialog
        {
            Description = toPng ? "Папка с BMP для конвертации в PNG" : "Папка с PNG для конвертации в BMP"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string folder = dlg.SelectedPath;
        await RunBusy(toPng ? "BMP → PNG…" : "PNG → BMP…", progress =>
        {
            var (ok, failed) = ImageBatch.ConvertFolder(folder, toPng, null, false, progress.Report);
            progress.Report($"Готово: {ok} ok, {failed} ошибок");
        });
    }

    // ------------------------------------------------------------- служебное

    private async Task RunBusy(string title, Action<IProgress<string>> work)
    {
        if (_busy) return;
        _busy = true;
        _toolbar.Enabled = false;
        _progress.Visible = true;
        _status.Text = title;
        Cursor = Cursors.WaitCursor;

        var progress = new Progress<string>(Log);
        try
        {
            await Task.Run(() => work(progress));
        }
        catch (Exception ex)
        {
            Log("ОШИБКА: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            _toolbar.Enabled = true;
            _progress.Visible = false;
            Cursor = Cursors.Default;
            UpdateStatus();
        }
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Log), message);
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        if (files.Length != 1) return;

        if (files[0].EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
            e.Effect = DragDropEffects.Copy;
        else if (Directory.Exists(files[0]))
            e.Effect = _file != null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        if (Directory.Exists(files[0]))
        {
            if (_file != null) ImportDialog(false, files[0]);
            else
            {
                var tex = Directory.GetFiles(files[0], "*.tex", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (tex != null) OpenFile(tex);
                else
                {
                    var inner = Directory.GetFiles(files[0], "*.tex", SearchOption.AllDirectories).FirstOrDefault();
                    if (inner != null) OpenFile(inner);
                }
            }
            return;
        }

        OpenFile(files[0]);
    }

    private void ShowFormatHelp()
    {
        MessageBox.Show(this,
            "Формат .tex (Manhunt 2)\r\n\r\n" +
            "Контейнер: TCDT (без сжатия) или Z2HM (zlib).\r\n" +
            "Внутри — связанный список узлов по 0x80 байт:\r\n" +
            "  +0x00 следующий узел, +0x04 предыдущий,\r\n" +
            "  +0x08 имя (64 байта), +0x48 параметры текстуры,\r\n" +
            "  +0x60 смещение DDS-блока, +0x68 размер блока.\r\n" +
            "Данные — обычный DDS: DXT1 / DXT5 / ARGB32.\r\n\r\n" +
            "При замене новый блок пишется в конец файла, а смещение\r\n" +
            "в узле обновляется. Если размер совпадает — на место.\r\n" +
            "Остальные текстуры и таблица релокаций не меняются.",
            "О формате .tex", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
