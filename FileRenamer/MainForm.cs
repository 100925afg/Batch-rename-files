using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FileRenamer
{
    public class MainForm : Form
    {
        // ============ 数据 ============
        private class FileItem
        {
            public string FullPath;
            public string Dir;
            public string OldName;      // 含扩展名
            public string BaseName;     // 不含扩展名
            public string Ext;          // 含点
            public DateTime Modified;
            public long Size;
            public string NewName;      // 预览结果（含扩展名）
        }

        private readonly List<FileItem> _files = new List<FileItem>();
        private List<FileItem> _view = new List<FileItem>(); // 排序后的视图

        // ============ 控件 ============
        private ListView lv;
        private Button btnAddFiles, btnAddFolder, btnRemove, btnClear, btnPreview, btnExecute;
        private CheckBox chkSubDir, chkSeqEnable, chkRepEnable, chkRegex, chkStripOldSeq;
        private NumericUpDown numStart, numDigits, numStep;
        private TextBox txtSep, txtFind, txtReplace;
        private RadioButton rbPrefix, rbSuffix;
        private Label lblStatus;
        private ColumnHeader colOld, colNew, colSize, colTime, colDir;

        // ============ 排序状态（点击列头切换） ============
        private int _sortCol = 0;      // 0=文件名 2=大小 3=修改时间
        private bool _sortAsc = true;

        public MainForm()
        {
            Text = "批量文件重命名工具";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(980, 680);
            MinimumSize = new Size(860, 600);
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;

            BuildLayout();
        }

        private void BuildLayout()
        {
            // ---- 顶部：文件操作 ----
            var pnlTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6, 5, 6, 0), WrapContents = false, AutoSize = false };
            btnAddFiles = MakeBtn("添加文件…", (s, e) => AddFiles());
            btnAddFolder = MakeBtn("添加文件夹…", (s, e) => AddFolder());
            btnRemove = MakeBtn("移除选中", (s, e) => RemoveSelected());
            btnClear = MakeBtn("清空列表", (s, e) => { _files.Clear(); RefreshView(); });
            chkSubDir = new CheckBox { Text = "包含子文件夹", AutoSize = true, Margin = new Padding(14, 9, 3, 3) };
            pnlTop.Controls.AddRange(new Control[] { btnAddFiles, btnAddFolder, btnRemove, btnClear, chkSubDir });

            // ---- 中部：列表（虚拟模式，支持 10 万+ 文件不卡） ----
            lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                VirtualMode = true,
                MultiSelect = true,
                GridLines = true,
            };
            colOld = new ColumnHeader { Text = "原文件名（点击排序）", Width = 260 };
            colNew = new ColumnHeader { Text = "新文件名（预览）", Width = 260 };
            colSize = new ColumnHeader { Text = "大小（点击排序）", Width = 100, TextAlign = HorizontalAlignment.Right };
            colTime = new ColumnHeader { Text = "修改时间（点击排序）", Width = 150 };
            colDir = new ColumnHeader { Text = "所在目录", Width = 260 };
            lv.Columns.AddRange(new[] { colOld, colNew, colSize, colTime, colDir });
            lv.RetrieveVirtualItem += (s, e) =>
            {
                var f = _view[e.ItemIndex];
                e.Item = new ListViewItem(new[]
                {
                    f.OldName,
                    f.NewName ?? "",
                    FormatSize(f.Size),
                    f.Modified.ToString("yyyy-MM-dd HH:mm:ss"),
                    f.Dir
                });
                if (!string.IsNullOrEmpty(f.NewName) && f.NewName != f.OldName)
                    e.Item.ForeColor = Color.DarkBlue;
            };
            // 点击列头排序：再次点击同一列切换升/降序
            lv.ColumnClick += (s, e) =>
            {
                int col = e.Column;
                if (col != 0 && col != 2 && col != 3) return; // 仅 文件名/大小/时间 可排序
                if (_sortCol == col) _sortAsc = !_sortAsc;
                else { _sortCol = col; _sortAsc = true; }
                RefreshView();
            };

            // ---- 底部设置区 ----
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 240, Padding = new Padding(8) };

            // 功能1：序号
            var grpSeq = new GroupBox { Text = "功能一：添加排序序号", Dock = DockStyle.Top, Height = 106 };
            chkSeqEnable = new CheckBox { Text = "启用", Left = 14, Top = 24, AutoSize = true };
            var lbl1 = new Label { Text = "起始：", Left = 110, Top = 26, AutoSize = true };
            numStart = MakeNum(1, 0, 999999999, 110, 46);
            var lbl2 = new Label { Text = "位数：", Left = 205, Top = 26, AutoSize = true };
            numDigits = MakeNum(4, 1, 10, 205, 46);
            var lbl3 = new Label { Text = "步长：", Left = 300, Top = 26, AutoSize = true };
            numStep = MakeNum(1, 1, 1000, 300, 46);
            var lbl4 = new Label { Text = "分隔符：", Left = 395, Top = 26, AutoSize = true };
            txtSep = new TextBox { Left = 395, Top = 46, Width = 70, Text = " - " };
            rbPrefix = new RadioButton { Text = "加在开头", Left = 495, Top = 48, Width = 110, Checked = true };
            rbSuffix = new RadioButton { Text = "加在末尾", Left = 615, Top = 48, Width = 110 };
            chkStripOldSeq = new CheckBox { Text = "先去除原有序号前缀（如 001_、01-）", Left = 14, Top = 78, AutoSize = true, Checked = true };
            grpSeq.Controls.AddRange(new Control[] { chkSeqEnable, lbl1, numStart, lbl2, numDigits, lbl3, numStep, lbl4, txtSep, rbPrefix, rbSuffix, chkStripOldSeq });

            // 功能2：字段替换（分两行，避免输入框被遮挡）
            var grpRep = new GroupBox { Text = "功能二：部分字段替换重命名", Dock = DockStyle.Top, Height = 92 };
            chkRepEnable = new CheckBox { Text = "启用", Left = 14, Top = 24, AutoSize = true };
            chkRegex = new CheckBox { Text = "使用正则表达式", Left = 80, Top = 24, AutoSize = true };
            var lblF = new Label { Text = "查找：", Left = 14, Top = 58, AutoSize = true };
            txtFind = new TextBox { Left = 60, Top = 54, Width = 340 };
            var lblR = new Label { Text = "替换为：", Left = 420, Top = 58, AutoSize = true };
            txtReplace = new TextBox { Left = 480, Top = 54, Width = 340 };
            grpRep.Controls.AddRange(new Control[] { chkRepEnable, chkRegex, lblF, txtFind, lblR, txtReplace });

            // 执行按钮 + 状态
            var pnlRun = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            btnPreview = MakeBtn("生成预览", (s, e) => DoPreview());
            btnExecute = MakeBtn("执行重命名", (s, e) => DoExecute());
            btnExecute.BackColor = Color.FromArgb(0, 120, 215);
            btnExecute.ForeColor = Color.White;
            lblStatus = new Label { AutoSize = false, Left = 240, Top = 6, Width = 640, Height = 36, Text = "就绪", TextAlign = ContentAlignment.MiddleLeft };
            pnlRun.Controls.AddRange(new Control[] { btnPreview, btnExecute, lblStatus });
            btnPreview.Left = 8; btnPreview.Top = 8;
            btnExecute.Left = 130; btnExecute.Top = 8;

            pnlBottom.Controls.Add(pnlRun);      // 固定在底部
            pnlBottom.Controls.Add(grpRep);
            pnlBottom.Controls.Add(grpSeq);
            // 从上到下：功能一 → 功能二
            pnlBottom.Controls.SetChildIndex(grpSeq, 0);
            pnlBottom.Controls.SetChildIndex(grpRep, 1);

            Controls.Add(lv);
            Controls.Add(pnlBottom);
            Controls.Add(pnlTop);
        }

        private Button MakeBtn(string text, EventHandler handler)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 2, 10, 2),
                Margin = new Padding(4),
                MinimumSize = new Size(0, 30),
            };
            b.Click += handler;
            return b;
        }

        private NumericUpDown MakeNum(int val, int min, int max, int left, int top)
        {
            return new NumericUpDown { Value = val, Minimum = min, Maximum = max, Left = left, Top = top, Width = 80 };
        }

        // ============ 文件加载 ============
        private void AddFiles()
        {
            using (var dlg = new OpenFileDialog { Multiselect = true, Title = "选择文件（可多选）" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                AddPaths(dlg.FileNames);
            }
        }

        private void AddFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "选择文件夹" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var opt = chkSubDir.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                try
                {
                    var paths = Directory.EnumerateFiles(dlg.SelectedPath, "*.*", opt);
                    AddPaths(paths);
                }
                catch (Exception ex) { MessageBox.Show(this, "读取文件夹失败：" + ex.Message); }
            }
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            var existing = new HashSet<string>(_files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var p in paths)
                {
                    if (existing.Contains(p)) continue;
                    FileInfo fi;
                    try { fi = new FileInfo(p); } catch { continue; }
                    if (!fi.Exists) continue;
                    _files.Add(new FileItem
                    {
                        FullPath = fi.FullName,
                        Dir = fi.DirectoryName,
                        OldName = fi.Name,
                        BaseName = Path.GetFileNameWithoutExtension(fi.Name),
                        Ext = fi.Extension,
                        Modified = fi.LastWriteTime,
                        Size = fi.Length,
                    });
                    existing.Add(p);
                    added++;
                }
            }
            finally { Cursor = Cursors.Default; }
            RefreshView();
            lblStatus.Text = $"已添加 {added} 个文件，共 {_files.Count} 个";
        }

        private void RemoveSelected()
        {
            if (lv.SelectedIndices.Count == 0) return;
            var toRemove = lv.SelectedIndices.Cast<int>().Select(i => _view[i]).ToHashSet();
            _files.RemoveAll(f => toRemove.Contains(f));
            RefreshView();
        }

        // ============ 排序与视图 ============
        private void RefreshView()
        {
            IEnumerable<FileItem> q = _files;
            bool asc = _sortAsc;
            switch (_sortCol)
            {
                case 2: q = asc ? q.OrderBy(f => f.Size) : q.OrderByDescending(f => f.Size); break;
                case 3: q = asc ? q.OrderBy(f => f.Modified) : q.OrderByDescending(f => f.Modified); break;
                default: q = asc ? q.OrderBy(f => f.OldName, StringComparer.CurrentCultureIgnoreCase) : q.OrderByDescending(f => f.OldName, StringComparer.CurrentCultureIgnoreCase); break;
            }
            _view = q.ToList();
            lv.VirtualListSize = _view.Count;
            lv.Invalidate();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576.0).ToString("0.0") + " MB";
            return (bytes / 1073741824.0).ToString("0.00") + " GB";
        }

        // ============ 新文件名生成 ============
        private static readonly Regex OldSeqPattern = new Regex(@"^\s*\d{1,10}\s*[-_\.、\s]+", RegexOptions.Compiled);

        private string BuildNewName(FileItem f, int index)
        {
            string baseName = f.BaseName;

            // 功能二：字段替换
            if (chkRepEnable.Checked && txtFind.Text.Length > 0)
            {
                try
                {
                    baseName = chkRegex.Checked
                        ? Regex.Replace(baseName, txtFind.Text, txtReplace.Text)
                        : baseName.Replace(txtFind.Text, txtReplace.Text);
                }
                catch (Exception ex)
                {
                    throw new Exception("替换规则错误：" + ex.Message);
                }
            }

            // 功能一：序号
            if (chkSeqEnable.Checked)
            {
                if (chkStripOldSeq.Checked)
                    baseName = OldSeqPattern.Replace(baseName, "");
                long n = (long)numStart.Value + index * (long)numStep.Value;
                string seq = n.ToString("D" + (int)numDigits.Value);
                baseName = rbPrefix.Checked ? seq + txtSep.Text + baseName : baseName + txtSep.Text + seq;
            }

            // 清理非法字符（替换结果可能引入）
            foreach (char c in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(c, '_');

            return baseName + f.Ext;
        }

        private bool DoPreview()
        {
            if (_view.Count == 0) { lblStatus.Text = "列表为空"; return false; }
            if (!chkSeqEnable.Checked && !chkRepEnable.Checked)
            {
                MessageBox.Show(this, "请至少启用一个功能（序号 或 字段替换）。");
                return false;
            }
            try
            {
                for (int i = 0; i < _view.Count; i++)
                    _view[i].NewName = BuildNewName(_view[i], i);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message);
                return false;
            }

            // 冲突检测：同目录下新名重复 或 与列表外文件撞名
            var groups = _view.GroupBy(f => f.Dir + "|" + f.NewName, StringComparer.OrdinalIgnoreCase)
                              .Where(g => g.Count() > 1).ToList();
            int external = 0;
            var inList = new HashSet<string>(_view.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
            foreach (var f in _view)
            {
                string target = Path.Combine(f.Dir, f.NewName);
                if (!inList.Contains(target) && File.Exists(target) && !target.Equals(f.FullPath, StringComparison.OrdinalIgnoreCase))
                    external++;
            }

            lv.Invalidate();
            if (groups.Count > 0 || external > 0)
            {
                lblStatus.Text = $"预览完成：{_view.Count} 个文件，发现 {groups.Count} 组重名冲突、{external} 个与磁盘文件冲突，请调整规则！";
                lblStatus.ForeColor = Color.Red;
                return false;
            }
            lblStatus.ForeColor = Color.DarkGreen;
            lblStatus.Text = $"预览完成：{_view.Count} 个文件，无冲突，可以执行。";
            return true;
        }

        // ============ 执行 ============
        private void DoExecute()
        {
            if (_view.Count == 0) return;
            if (_view.Any(f => f.NewName == null) && !DoPreview()) return;

            var changed = _view.Where(f => !f.NewName.Equals(f.OldName, StringComparison.Ordinal)).ToList();
            if (changed.Count == 0) { lblStatus.Text = "没有需要重命名的文件。"; return; }

            var r = MessageBox.Show(this,
                $"即将重命名 {changed.Count} 个文件，是否继续？",
                "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            int ok = 0, fail = 0;
            var log = new StringBuilder();
            Cursor = Cursors.WaitCursor;
            btnExecute.Enabled = false;
            try
            {
                // 两阶段重命名，避免列表内互换名字时互相覆盖
                var tempMap = new List<Tuple<FileItem, string>>();
                foreach (var f in changed)
                {
                    try
                    {
                        string tmp = Path.Combine(f.Dir, ".__ren_tmp__" + Guid.NewGuid().ToString("N") + f.Ext);
                        File.Move(f.FullPath, tmp);
                        tempMap.Add(Tuple.Create(f, tmp));
                    }
                    catch (Exception ex) { fail++; log.AppendLine($"[失败-暂存] {f.OldName}：{ex.Message}"); }
                }
                foreach (var t in tempMap)
                {
                    var f = t.Item1;
                    try
                    {
                        string target = Path.Combine(f.Dir, f.NewName);
                        File.Move(t.Item2, target);
                        f.FullPath = target; f.OldName = f.NewName;
                        f.BaseName = Path.GetFileNameWithoutExtension(f.NewName);
                        ok++;
                    }
                    catch (Exception ex) { fail++; log.AppendLine($"[失败] {f.OldName} → {f.NewName}：{ex.Message}"); }
                }
            }
            finally
            {
                Cursor = Cursors.Default;
                btnExecute.Enabled = true;
            }

            RefreshView();
            lblStatus.ForeColor = fail == 0 ? Color.DarkGreen : Color.Red;
            lblStatus.Text = $"完成：成功 {ok} 个，失败 {fail} 个。";
            if (fail > 0)
                MessageBox.Show(this, log.ToString(), "失败明细", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
