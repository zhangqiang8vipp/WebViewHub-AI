using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace WebViewHub.Controls
{
    public partial class WebViewContainer : UserControl
    {
        #region 依赖属性

        public static readonly DependencyProperty ProfileIDProperty =
            DependencyProperty.Register(
                nameof(ProfileID),
                typeof(string),
                typeof(WebViewContainer),
                new PropertyMetadata(string.Empty, OnProfileIDChanged));

        public string ProfileID
        {
            get => (string)GetValue(ProfileIDProperty);
            set => SetValue(ProfileIDProperty, value);
        }

        public static readonly DependencyProperty ProfileNameProperty =
            DependencyProperty.Register(
                nameof(ProfileName),
                typeof(string),
                typeof(WebViewContainer),
                new PropertyMetadata("Profile"));

        public string ProfileName
        {
            get => (string)GetValue(ProfileNameProperty);
            set => SetValue(ProfileNameProperty, value);
        }

        public static readonly DependencyProperty RoleTagProperty =
            DependencyProperty.Register(
                nameof(RoleTag),
                typeof(string),
                typeof(WebViewContainer),
                new PropertyMetadata(string.Empty));

        public string RoleTag
        {
            get => (string)GetValue(RoleTagProperty);
            set => SetValue(RoleTagProperty, value);
        }

        public string CurrentUrl
        {
            get => WebView.CurrentUrl;
            set => WebView.CurrentUrl = value;
        }

        public static readonly DependencyProperty ZoomFactorProperty =
            DependencyProperty.Register(
                nameof(ZoomFactor),
                typeof(double),
                typeof(WebViewContainer),
                new PropertyMetadata(1.0, OnZoomFactorChanged));

        public double ZoomFactor
        {
            get => (double)GetValue(ZoomFactorProperty);
            set => SetValue(ZoomFactorProperty, value);
        }

        #endregion

        #region 事件

        public event EventHandler<CustomDragDeltaEventArgs>? CustomDragDelta;
        public event EventHandler<CustomResizeDeltaEventArgs>? CustomResizeDelta;
        public event EventHandler<WebViewContainer>? DeleteRequested;

        #endregion

        public WebViewContainer()
        {
            InitializeComponent();
            SizeChanged += WebViewContainer_SizeChanged;
            WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
        }

        private void WebView_CoreWebView2InitializationCompleted(object? sender, EventArgs e)
        {
            var coreWebView = WebView.GetCoreWebView2();
            if (coreWebView != null)
            {
                ApplyMobileModeUI();
                coreWebView.Settings.UserAgent = _isMobileMode ? MobileUserAgent : DesktopUserAgent;
            }
        }

        private void WebViewContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            // 当容器大小变化或初始加载时计算缩放比例并应用
            // 以 1000 作为基准宽度，低于这个宽度就按比例缩小（自动缩放部分）
            double baseWidth = 1000.0;
            double currentWidth = ActualWidth;
            if (currentWidth <= 0) return;

            double autoZoom = Math.Min(1.0, currentWidth / baseWidth);
            autoZoom = Math.Max(0.3, autoZoom);

            // 最终缩放比例 = 自动缩放因子 * 用户手动持久化的缩放因子
            double finalZoom = autoZoom * ZoomFactor;

            var coreWebView = WebView.GetCoreWebView2();
            if (coreWebView != null)
            {
                WebView.SetZoomFactor(finalZoom);
            }
        }

        private static void OnZoomFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebViewContainer container)
            {
                container.ApplyZoom();
            }
        }

        private static void OnProfileIDChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebViewContainer container && !string.IsNullOrEmpty(e.NewValue as string))
            {
                container.ProfileName = (string)e.NewValue;
            }
        }

        #region 拖拽与调整大小逻辑 (原生 Thumb)

        private void DragThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            CustomDragDelta?.Invoke(this, new CustomDragDeltaEventArgs(e.HorizontalChange, e.VerticalChange));
        }

        private void ResizeRight_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            var newWidth = Math.Max(300, Width + e.HorizontalChange);
            var args = new CustomResizeDeltaEventArgs(newWidth, Height);
            CustomResizeDelta?.Invoke(this, args);
            if (args.Handled) return;
            Width = newWidth;
        }

        private void ResizeBottom_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            var newHeight = Math.Max(200, Height + e.VerticalChange);
            var args = new CustomResizeDeltaEventArgs(Width, newHeight);
            CustomResizeDelta?.Invoke(this, args);
            if (args.Handled) return;
            Height = newHeight;
        }

        private void ResizeBottomRight_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            var newWidth = Math.Max(300, Width + e.HorizontalChange);
            var newHeight = Math.Max(200, Height + e.VerticalChange);
            var args = new CustomResizeDeltaEventArgs(newWidth, newHeight);
            CustomResizeDelta?.Invoke(this, args);
            if (args.Handled) return;
            Width = newWidth;
            Height = newHeight;
        }

        #endregion

        #region 地址配置与删除

        private void EditRole_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AppleRoleDialog(RoleTag)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                RoleTag = dialog.RoleTag;
                (Window.GetWindow(this) as MainWindow)?.SaveLayout();
            }
        }

        private void EditUrl_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AppleUrlDialog(CurrentUrl)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                var newUrl = dialog.Url;
                if (CurrentUrl != newUrl)
                {
                    CurrentUrl = newUrl;
                    WebView.Navigate(newUrl);
                    (Window.GetWindow(this) as MainWindow)?.SaveLayout();
                }
            }
        }

        private bool _isMobileMode = false;
        public bool IsMobileModeContent
        {
            get => _isMobileMode;
            set
            {
                if (_isMobileMode != value)
                {
                    _isMobileMode = value;
                    ApplyMobileModeUI();
                }
            }
        }

        // 手机版 UA（iPhone Safari）
        private const string MobileUserAgent =
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) " +
            "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

        // 桌面版 UA（Chrome Windows）
        private const string DesktopUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";

        private void ApplyMobileModeUI()
        {
            ToggleMobileButton.ToolTip = _isMobileMode ? "当前：手机版（点击切换桌面版）" : "切换手机版/桌面版";
            ToggleMobileButton.Background = _isMobileMode
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 255))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(191, 191, 191));
        }

        private async void ToggleMobile_Click(object sender, RoutedEventArgs e)
        {
            var coreWebView = WebView.GetCoreWebView2();
            if (coreWebView == null) return;

            IsMobileModeContent = !_isMobileMode;

            // 切换 User-Agent
            coreWebView.Settings.UserAgent = _isMobileMode ? MobileUserAgent : DesktopUserAgent;

            // 存入配置文件
            (Window.GetWindow(this) as MainWindow)?.SaveLayout();

            // 刷新页面以应用新 UA
            await coreWebView.ExecuteScriptAsync("location.reload();");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            DeleteRequested?.Invoke(this, this);
        }

        #endregion

        #region 注入与中转机制 (AI 群聊总线)

        /// <summary>
        /// 针对主流大模型网页（豆包、Grok、Gemini 等），寻找文本输入框并模拟输入和回车发送
        /// 策略：找到正确输入框 → 填入文本 → 触发 React/Vue 事件 → 模拟 Enter 发送（不找按钮）
        /// </summary>
        public async Task InjectAndSendAsync(string text)
        {
            var coreWebView = WebView.GetCoreWebView2();
            if (coreWebView == null) return;

            // 1. 第一步：先将窗口焦点系统级赋予给 WebView，这对底层渲染引擎接收按键至关重要。
            await Application.Current.Dispatcher.InvokeAsync(() => WebView.Focus());
            await Task.Delay(100);

            // 2. 第二步：在前端寻找到输入框 -> 赐予前端焦点光标 -> 执行清空操作
            string scriptFocus = @"
                (function() {
                    let el = document.querySelector('.ql-editor[contenteditable=""true""]') ||
                             document.querySelector('div[aria-label=""Enter a prompt for Gemini""]') ||
                             document.querySelector('div[contenteditable=""true""][aria-label]') ||
                             document.querySelector('div#prompt-textarea') ||
                             document.querySelector('div.ProseMirror') ||
                             document.querySelector('textarea[data-testid=""chat_input_input""]') ||
                             document.querySelector('textarea[aria-label=""Enter a prompt for Gemini""]') ||
                             document.querySelector('textarea[placeholder*=""消息""]') ||
                             document.querySelector('textarea[placeholder*=""输入""]') ||
                             document.querySelector('textarea[aria-label*=""message"" i]') ||
                             document.querySelector('textarea');
                             
                    if (el) {
                        el.focus();
                        el.click(); // 骗过某些绑在 click 上的激活状态
                        if (el.isContentEditable) {
                            const sel = window.getSelection();
                            const range = document.createRange();
                            range.selectNodeContents(el);
                            sel.removeAllRanges();
                            sel.addRange(range);
                            document.execCommand('delete', false); // 清空
                        } else {
                            el.value = ''; // text area 清空
                            el.dispatchEvent(new Event('input', { bubbles: true }));
                        }
                        return 'true';
                    }
                    return 'false';
                })();
            ";
            
            var focusResult = await coreWebView.ExecuteScriptAsync(scriptFocus);
            // 这里返回 ""true""，带双引号。如果是 false 就不再注入。
            if (focusResult != "\"true\"") return;

            // 等待前端生命周期和清空动画完结
            await Task.Delay(200);

            // 3. 第三步：绝杀！调用 Chromium 引擎底层的开发者协议 (CDP) 进行注入。
            // 这种方式直接绕过页面的 JS 环境，相当于直接在浏览器内核挂载了物理钩子敲击键盘。
            // 将文本安全地转为 JSON 字符串如 "你好\n世界"
            var safeText = System.Text.Json.JsonSerializer.Serialize(text);
            string insertTextJson = $"{{\"text\": {safeText}}}";
            await coreWebView.CallDevToolsProtocolMethodAsync("Input.insertText", insertTextJson);

            // 让 React / Angular 完全消化这些“键盘敲击”
            await Task.Delay(300);

            // 4. 第四步：CDP 模拟完美的实体 Enter 回车键按下与抬起
            // 检测是否需要 Ctrl+Enter 组合键 (Grok 与 Gemini 等新版本网页特性强制要求)
            bool requiresCtrl = false;
            try
            {
                // 使用 coreWebView.Source (string) 并解析为其 Host
                var uri = new Uri(coreWebView.Source);
                string host = uri.Host.ToLower();
                if (host.Contains("grok") || host.Contains("x.com") || host.Contains("gemini"))
                {
                    requiresCtrl = true;
                }
            }
            catch { }

            string modifierJson = requiresCtrl ? ", \"modifiers\": 2" : "";
            
            string keyDownJson = $"{{\"type\": \"keyDown\", \"windowsVirtualKeyCode\": 13, \"key\": \"Enter\", \"code\": \"Enter\", \"text\": \"\\r\"{modifierJson}}}";
            string keyUpJson = $"{{\"type\": \"keyUp\", \"windowsVirtualKeyCode\": 13, \"key\": \"Enter\", \"code\": \"Enter\"{modifierJson}}}";
            
            await coreWebView.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", keyDownJson);
            await Task.Delay(50);
            await coreWebView.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", keyUpJson);
        }

        /// <summary>供外部（如 CentralCommandPanel）获取 CoreWebView2 实例，用于执行监控脚本。</summary>
        public Microsoft.Web.WebView2.Core.CoreWebView2? GetCoreWebView2() => WebView.GetCoreWebView2();

        // 抓取限流器：防止并发触发抓取造成剪贴板竞态
        private static readonly System.Threading.SemaphoreSlim _fetchLock = new System.Threading.SemaphoreSlim(1, 1);

        /// <summary>
        /// 基于 Turndown DOM 抓取方案获取最后一条回复的 Markdown 原文。
        /// 通过注入 TurndownService.js 与本地的高级多平台适配器，不仅省却了操作虚拟鼠标与系统剪贴板的复杂步骤，
        /// 更通过 HTML 逆向实现了对代码块、富文本高保真解析，彻底摆脱跨模型的不同格式截断问题。
        /// </summary>
        public async Task<string> FetchLastResponseAsync()
        {
            var coreWebView = WebView.GetCoreWebView2();
            if (coreWebView == null) return string.Empty;

            await _fetchLock.WaitAsync();
            try
            {
                // 1. 读取随应用程序分发的两份 JS 资源核心框架
                string turndownPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "turndown.js");
                string extractorPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extractor.js");

                if (!System.IO.File.Exists(turndownPath) || !System.IO.File.Exists(extractorPath))
                {
                    return "❌ 本地核心解析 JS 资源文件丢失。";
                }

                string turndownJs = await System.IO.File.ReadAllTextAsync(turndownPath);
                string extractorJs = await System.IO.File.ReadAllTextAsync(extractorPath);

                // 2. 将框架层无损注入至当前 WebView 当前作用域中；先探测是否已有 TurndownService，若无则运行
                string checkScript = "typeof window.TurndownService !== 'undefined'";
                var hasTurndown = await coreWebView.ExecuteScriptAsync(checkScript);
                
                if (hasTurndown != "true")
                {
                    // 必须抛弃外层大括号，使得 var TurndownService 暴露至 Global
                    await coreWebView.ExecuteScriptAsync(turndownJs + ";\nwindow.TurndownService = TurndownService;");
                }

                // 3. 聚焦网页（某些懒加载策略可能基于 visibility），并调用提取器拉起多平台兼容的 DOM 到 MD 的渲染过程
                await Application.Current.Dispatcher.InvokeAsync(() => WebView.Focus());
                await Task.Delay(150);

                var jsonRaw = await coreWebView.ExecuteScriptAsync(extractorJs);

                if (string.IsNullOrEmpty(jsonRaw) || jsonRaw == "null" || jsonRaw == "\"\"")
                    return string.Empty;

                // 4. 对返回值解包转出到 C# 原生 String 并返回
                string markdown;
                try 
                { 
                    markdown = System.Text.Json.JsonSerializer.Deserialize<string>(jsonRaw) ?? ""; 
                }
                catch 
                { 
                    markdown = jsonRaw.Trim('"'); 
                }

                // 解除转义换行符并检测错误标志
                markdown = markdown.Replace("\\n", "\n").Replace("\\r", "\r");
                if (markdown.StartsWith("Error:"))
                {
                    return "❌ " + markdown.Trim();
                }

                return markdown.Trim();
            }
            catch (Exception)
            {
                return string.Empty;
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        #endregion

        #region 清理

        public void Cleanup()
        {
            WebView?.Cleanup();
        }

        #endregion
    }

    #region 事件参数

    public class CustomDragDeltaEventArgs : EventArgs
    {
        public double HorizontalChange { get; set; }
        public double VerticalChange { get; set; }

        public CustomDragDeltaEventArgs(double horizontalChange, double verticalChange)
        {
            HorizontalChange = horizontalChange;
            VerticalChange = verticalChange;
        }
    }

    public class CustomResizeDeltaEventArgs : EventArgs
    {
        public double NewWidth { get; set; }
        public double NewHeight { get; set; }
        public bool Handled { get; set; }

        public CustomResizeDeltaEventArgs(double newWidth, double newHeight)
        {
            NewWidth = newWidth;
            NewHeight = newHeight;
        }
    }

    #endregion
}
