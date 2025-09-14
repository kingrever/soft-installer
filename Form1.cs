#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace SoftwareInstaller
{
    public partial class Form1 : Form
    {
        // ====== 功能配置（按需修改） ======
        private string _sharePath = @"\\10.1.4.8\资源文件\Software"; // 群晖共享路径
        private readonly bool _useCredentials = true;               // 进程级凭据模拟（不污染系统会话）
        private readonly string? _domain = null;                    // 群晖服务器名/域；不确定就设为 null
        private readonly string _username = "soft-installer";            // 账号
        private readonly string _password = "jjzzx123";         // 密码
        private readonly string _catalogFileName = "catalog.json";  // 可选软件清单
        private readonly string _defaultMsiArgs = "/qn /norestart"; // MSI 默认参数
        private readonly string _defaultExeArgs = "";               // EXE 默认参数（多放在 catalog 里）
        // ==================================

        // —— 设计系统（颜色/尺寸/字体）——
        private static Color C(string hex) => ColorTranslator.FromHtml(hex);
        private readonly Color C_BG         = C("#F7F7FA"); // 背景
        private readonly Color C_Card       = C("#FFFFFF"); // 卡片/输入背景
        private readonly Color C_Text       = C("#0F172A"); // 主文字
        private readonly Color C_SubText    = C("#667085"); // 次级文字
        private readonly Color C_Line       = C("#E5E7EB"); // 分割线
        private readonly Color C_Primary    = C("#2970FF"); // 主按钮
        private readonly Color C_PrimaryHov = C("#1E5AE8"); // 主按钮 Hover

        private readonly Font F_Title   = new("Segoe UI Semibold", 20f);
        private readonly Font F_Head    = new("Segoe UI Semibold", 12.5f);
        private readonly Font F_Body    = new("Segoe UI", 11f);
        private readonly Font F_Caption = new("Segoe UI", 10f);

        // —— 顶部/工具区/内容/状态 —— 
        private Panel _appBar = default!;
        private Label _appTitle = default!;
        private ModernButton _btnRefresh = default!;

        private Panel _content = default!;
        private FlowLayoutPanel _list = default!;  // 竖向堆叠的行卡片

        private StatusStrip _statusStrip = default!;
        private ToolStripStatusLabel _status = default!;
        private ToolStripProgressBar _progress = default!;

        // 数据
        private Catalog _catalog = new();

        public Form1()
        {
            base.Text = "软件列表";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(980, 820);
            MinimumSize = new Size(980, 820);
            BackColor = C_BG;
            Font = F_Body;

            BuildUI();

            Shown += async (_, __) => await LoadListAsync();
        }

        // ----------------- 构建 UI -----------------
        private void BuildUI()
        {
            // 顶栏
            _appBar = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = C_Card, Padding = new Padding(24, 14, 24, 14) };
            Controls.Add(_appBar);

            _appTitle = new Label { Text = "软件列表", Dock = DockStyle.Left, AutoSize = true, Font = F_Title, ForeColor = C_Text, Padding = new Padding(0, 4, 16, 4) };
            _appBar.Controls.Add(_appTitle);

            _btnRefresh = new ModernButton
            {
                Text = "刷新列表",
                Dock = DockStyle.Right,
                Width = 120,
                Height = 38,
                BackColor = C_Primary,
                HoverBackColor = C_PrimaryHov,
                ForeColor = Color.White,
                Radius = 10,
                Margin = new Padding(0)
            };
            _btnRefresh.Click += async (_, __) => await LoadListAsync();
            _appBar.Controls.Add(_btnRefresh);

            // 内容区（滚动列表）
            _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 12), BackColor = C_BG };
            _list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = C_BG
            };
            _list.SizeChanged += (_, __) => AdjustRowWidths();
            _content.Controls.Add(_list);
            Controls.Add(_content);

            // 状态条
            _statusStrip = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false, BackColor = C_Card };
            _status = new ToolStripStatusLabel("就绪") { ForeColor = C_SubText };
            _progress = new ToolStripProgressBar { Style = ProgressBarStyle.Marquee, Visible = false, Alignment = ToolStripItemAlignment.Right };
            _statusStrip.Items.Add(_status);
            _statusStrip.Items.Add(_progress);
            Controls.Add(_statusStrip);
        }

        // ----------------- 数据加载 -----------------
        internal async Task LoadListAsync(Action<int>? threadObserver = null)
        {
            var share = _sharePath.Trim();
            _status.Text = "加载中…";
            _progress.Visible = true;

            try
            {
                var rows = await Task.Run(() =>
                {
                    threadObserver?.Invoke(Thread.CurrentThread.ManagedThreadId);
                    var list = new List<RowInfo>();

                    void Work()
                    {
                        _catalog = TryLoadCatalog(Path.Combine(share, _catalogFileName));
                        if (!Directory.Exists(share)) throw new IOException("无法访问共享路径：" + share);

                        foreach (var path in Directory.EnumerateFiles(share, "*.*", SearchOption.TopDirectoryOnly)
                                 .Where(p => new[] { ".msi", ".exe" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                                 .OrderBy(Path.GetFileName))
                        {
                            var fi = new FileInfo(path);
                            var size = fi.Length;
                            var fileName = Path.GetFileName(path);
                            var (disp, ver) = GetDisplayAndVersion(path, _catalog);
                            var detail = $"{(string.IsNullOrWhiteSpace(ver) ? "" : "v " + ver + " · ")}{Path.GetExtension(path).ToLower()} · {(size / 1024.0 / 1024.0):F1} MB";
                            var desc = _catalog.TryGet(fileName, out var ci2) ? (ci2.Description ?? "") : "";
                            var icon = _catalog.TryGet(fileName, out var ci) ? ci.Icon : null;
                            list.Add(new RowInfo(path, size, icon, disp, detail, desc));
                        }
                    }

                    if (_useCredentials)
                    {
                        using var imp = new ImpersonationHelper(_domain, _username, _password);
                        imp.Run(Work);
                    }
                    else
                    {
                        Work();
                    }

                    return list;
                });

                _list.Controls.Clear();

                foreach (var info in rows)
                {
                    var row = new ItemRow(
                        iconPath: info.Icon,
                        displayName: info.DisplayName,
                        detailLine: info.Detail,
                        description: info.Description,
                        buttonText: "安装",
                        primary: C_Primary,
                        primaryHover: C_PrimaryHov,
                        text: C_Text,
                        subText: C_SubText,
                        line: C_Line,
                        sizeBytes: info.SizeBytes
                    )

                    {
                        Tag = info.Path
                    };

                    row.OnPrimaryClick += async (_, __) =>
                    {

                        var fullPath = row.Tag as string;
                        if (!string.IsNullOrWhiteSpace(fullPath))
                            await InstallAsync(fullPath!);
                    };
                    row.OnCheckChanged += (_, __) => UpdateStatus();

                    _list.Controls.Add(row);

                    AdjustRowWidths();

                    await Task.Yield();
                }

                UpdateStatus();
            }
            catch (Exception ex)
            {
                _list.Controls.Clear();
                _status.Text = "加载失败";
                MessageBox.Show(ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _progress.Visible = false;
            }
        }


        internal void SetSharePath(string path) => _sharePath = path;

        private readonly record struct RowInfo(string Path, long SizeBytes, string? Icon, string DisplayName, string Detail, string Description);

        private void AdjustRowWidths()
        {
            foreach (var row in _list.Controls.OfType<ItemRow>())
                row.Width = _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
        }


        private void UpdateStatus()
        {
            int total = _list.Controls.Count;
            var selected = _list.Controls.OfType<ItemRow>().Where(r => r.Checked).ToList();
            int selCount = selected.Count;
            double selMb = selected.Sum(r => r.SizeBytes) / 1024.0 / 1024.0;
            _status.Text = $"已加载 {total} 个安装包，选中 {selCount} 个（合计 {selMb:F1} MB）";
        }

        // ----------------- 安装逻辑 -----------------
        private async Task InstallAsync(string remotePath)
        {
            try
            {
                _status.Text = "准备安装…" + Path.GetFileName(remotePath);

                var cacheRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SynoInstaller", "cache");
                Directory.CreateDirectory(cacheRoot);
                var localPath = Path.Combine(cacheRoot, Path.GetFileName(remotePath));

                void CopyFile()
                {
                    using var src = new FileStream(remotePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var dst = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    src.CopyTo(dst);
                }

                await Task.Run(() =>
                {
                    if (_useCredentials)
                    {
                        using var imp = new ImpersonationHelper(_domain, _username, _password);
                        imp.Run(CopyFile);
                    }
                    else
                    {
                        CopyFile();
                    }
                });

                // 额外参数
                var fileName = Path.GetFileName(localPath);
                string extraArgs = "";
                if (_catalog.TryGet(fileName, out var ci) && !string.IsNullOrWhiteSpace(ci.Args))
                    extraArgs = ci.Args!.Trim();

                var ext = Path.GetExtension(localPath).ToLowerInvariant();
                ProcessStartInfo psi;
                if (ext == ".msi")
                {
                    var args = $"/i \"{localPath}\" {_defaultMsiArgs} {extraArgs}".Trim();
                    psi = new ProcessStartInfo("msiexec.exe", args) { UseShellExecute = true, Verb = "runas" };
                }
                else
                {
                    var args = $"{_defaultExeArgs} {extraArgs}".Trim();
                    psi = new ProcessStartInfo(localPath, args) { UseShellExecute = true, Verb = "runas" };
                }

                using var p = Process.Start(psi);
                if (p != null && !p.HasExited) await Task.Run(() => p.WaitForExit());

                _status.Text = "安装完成（退出码：" + (p?.ExitCode.ToString() ?? "未知") + "）";
            }
            catch (Exception ex)
            {
                _status.Text = "安装失败";
                MessageBox.Show(ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ----------------- 名称/版本解析 -----------------
        private (string displayName, string? version) GetDisplayAndVersion(string path, Catalog catalog)
        {
            var file = Path.GetFileName(path);
            if (catalog.TryGet(file, out var ci))
                return (ci.DisplayName ?? file, ci.Version);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".msi")
            {
                try
                {
                    var name = MsiGetProperty(path, "ProductName");
                    var ver = MsiGetProperty(path, "ProductVersion");
                    if (string.IsNullOrWhiteSpace(name)) name = file;
                    return (name!, string.IsNullOrWhiteSpace(ver) ? null : ver);
                }
                catch { }
            }
            else if (ext == ".exe")
            {
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    var name = string.IsNullOrWhiteSpace(info.ProductName) ? file : info.ProductName!;
                    var ver = string.IsNullOrWhiteSpace(info.ProductVersion) ? null : info.ProductVersion!;
                    return (name, ver);
                }
                catch { }
            }
            return (file, null);
        }

        private Catalog TryLoadCatalog(string catalogPath)
        {
            try
            {
                if (File.Exists(catalogPath))
                {
                    var json = File.ReadAllText(catalogPath);
                    var cat = JsonSerializer.Deserialize<Catalog>(json) ?? new Catalog();
                    cat.BuildIndex();
                    return cat;
                }
            }
            catch { }
            return new Catalog();
        }

        // ----------------- 工具 -----------------
        private static string? MsiGetProperty(string msiPath, string prop)
        {
            IntPtr hProd = IntPtr.Zero;
            try
            {
                var r = NativeMsi.MsiOpenPackage(msiPath, out hProd);
                if (r != 0 || hProd == IntPtr.Zero) return null;

                int cap = 512;
                var sb = new StringBuilder(cap);
                var len = cap;
                var r2 = NativeMsi.MsiGetProperty(hProd, prop, sb, ref len);
                if (r2 == 234)
                {
                    sb = new StringBuilder(len + 1);
                    len = sb.Capacity;
                    r2 = NativeMsi.MsiGetProperty(hProd, prop, sb, ref len);
                }
                if (r2 == 0) return sb.ToString();
                return null;
            }
            finally
            {
                if (hProd != IntPtr.Zero) NativeMsi.MsiCloseHandle(hProd);
            }
        }
    }

    // —— 行卡片控件（按图实现）——
    internal sealed class ItemRow : Panel
    {
        public event EventHandler? OnPrimaryClick;
        public event EventHandler? OnCheckChanged;

        private readonly CheckBox _check;
        private readonly PictureBox _icon;
        private readonly Label _name;
        private readonly Label _detail;
        private readonly Label _desc;
        private readonly ModernButton _btn;

        private readonly Color _line;
        public long SizeBytes { get; }
        public bool Checked => _check.Checked;

        public ItemRow(string? iconPath, string displayName, string detailLine, string description,
                       string buttonText, Color primary, Color primaryHover, Color text, Color subText, Color line, long sizeBytes)
        {
            DoubleBuffered = true;
            Height = 92; // 与图相近
            BackColor = Color.Transparent;
            Padding = new Padding(12, 10, 12, 10);
            Margin = new Padding(0);
            _line = line;
            SizeBytes = sizeBytes;

            _check = new CheckBox
            {
                AutoSize = true,
                Location = new Point(12, 35)
            };
            _check.CheckedChanged += (s, e) => OnCheckChanged?.Invoke(this, EventArgs.Empty);
            Controls.Add(_check);

            _icon = new PictureBox
            {
                Size = new Size(44, 44),
                Location = new Point(44, 12),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                try { _icon.Image = Image.FromFile(iconPath); } catch { }
            }
            if (_icon.Image == null)
            {
                _icon.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using var b = new SolidBrush(Color.FromArgb(245, 247, 255));
                    using var pen = new Pen(Color.FromArgb(205, 215, 255));
                    var r = new Rectangle(0, 0, _icon.Width - 1, _icon.Height - 1);
                    e.Graphics.FillEllipse(b, r);
                    e.Graphics.DrawEllipse(pen, r);
                };
            }
            Controls.Add(_icon);

            _name = new Label
            {
                AutoSize = false,
                Location = new Point(100, 10),
                Size = new Size(608, 26),
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = text,
                Text = displayName
            };
            Controls.Add(_name);

            _detail = new Label
            {
                AutoSize = false,
                Location = new Point(100, 36),
                Size = new Size(608, 20),
                Font = new Font("Segoe UI", 10.5f),
                ForeColor = subText,
                Text = detailLine
            };
            Controls.Add(_detail);

            _desc = new Label
            {
                AutoSize = false,
                Location = new Point(100, 56),
                Size = new Size(608, 20),
                Font = new Font("Segoe UI", 10.5f),
                ForeColor = subText,
                Text = description,
            };
            Controls.Add(_desc);

            _btn = new ModernButton
            {
                Text = buttonText,
                Size = new Size(84, 36),
                Location = new Point(Width - 84 - 20, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = primary,
                HoverBackColor = primaryHover,
                ForeColor = Color.White,
                Radius = 10
            };
            _btn.Click += (s, e) => OnPrimaryClick?.Invoke(this, EventArgs.Empty);
            Controls.Add(_btn);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 下分割线
            using var pen = new Pen(_line, 1);
            e.Graphics.DrawLine(pen, new Point(12, Height - 1), new Point(Width - 12, Height - 1));
        }
    }

    // —— 绘图工具 —— 
    internal static class UiDrawing
    {
        internal static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void DrawRoundRect(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using var gp = RoundedRect(rect, radius);
            g.DrawPath(pen, gp);
        }
    }

    // —— 现代圆角按钮（抑制 Designer 警告）——
    internal sealed class ModernButton : Button
    {
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HoverBackColor { get; set; } = Color.Transparent;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = 8;

        private bool _hover;

        public ModernButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;

            MouseEnter += (_, __) => { _hover = true; Invalidate(); };
            MouseLeave += (_, __) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = ClientRectangle;

            using var path = UiDrawing.RoundedRect(rect, Radius);
            using var back = new SolidBrush(_hover ? HoverBackColor : BackColor);
            e.Graphics.FillPath(back, path);

            var sz = e.Graphics.MeasureString(Text, Font);
            e.Graphics.DrawString(Text, Font, new SolidBrush(ForeColor),
                (rect.Width - sz.Width) / 2f,
                (rect.Height - sz.Height) / 2f - 1);
        }
    }

    // —— 凭据模拟 —— 
    public sealed class ImpersonationHelper : IDisposable
    {
        private readonly SafeAccessTokenHandle _handle;

        public ImpersonationHelper(string? domain, string username, string password, int logonType = 9)
        {
            if (!LogonUser(username, domain ?? string.Empty, password, logonType, 3, out IntPtr rawHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _handle = new SafeAccessTokenHandle(rawHandle);
        }

        public void Run(Action action) => WindowsIdentity.RunImpersonated(_handle, action);
        public T Run<T>(Func<T> func) => WindowsIdentity.RunImpersonated(_handle, func);
        public void Dispose() => _handle.Dispose();

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword,
            int dwLogonType, int dwLogonProvider, out IntPtr phToken);
    }

    // —— MSI P/Invoke —— 
    internal static class NativeMsi
    {
        [DllImport("msi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint MsiOpenPackage(string szPackagePath, out IntPtr hProduct);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint MsiGetProperty(IntPtr hInstall, string szName, StringBuilder szValueBuf, ref int pchValueBuf);

        [DllImport("msi.dll", SetLastError = true)]
        public static extern uint MsiCloseHandle(IntPtr hAny);
    }

    // —— catalog.json —— 
    public class Catalog
    {
        public CatalogItem[] Items { get; set; } = Array.Empty<CatalogItem>();
        private System.Collections.Generic.Dictionary<string, CatalogItem>? _byFile;

        public void BuildIndex()
        {
            _byFile = Items?
                .Where(i => !string.IsNullOrWhiteSpace(i.FileName))
                .GroupBy(i => i.FileName!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
                ?? new System.Collections.Generic.Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase);
        }

        public bool TryGet(string fileName, out CatalogItem item)
        {
            if (_byFile == null) BuildIndex();
            var ok = _byFile!.TryGetValue(fileName, out var it);
            item = ok ? it! : new CatalogItem();
            return ok;
        }
    }

    public class CatalogItem
    {
        public string? FileName { get; set; }
        public string? DisplayName { get; set; }
        public string? Version { get; set; }
        public string? Args { get; set; }
        public string? Description { get; set; } // 新增：卡片次行说明
        public string? Icon { get; set; }        // 新增：图标路径（可选）
    }
}
