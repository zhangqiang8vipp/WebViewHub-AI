using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Markdig;
using Microsoft.Web.WebView2.Core;

namespace WebViewHub.Controls
{
    /// <summary>
    /// 回复卡片数据模型
    /// </summary>
    public class ResponseItem
    {
        public string Role { get; set; } = string.Empty;
        public string HtmlContent { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
    }

    public partial class CentralCommandPanel : UserControl
    {
        public MainWindow MainWindowReference { get; set; }
        private bool _isInsertingTag = false;

        // --- 回复展示集合 ---
        private readonly ObservableCollection<ResponseItem> _responses = new();

        // --- 标签栏状态 ---
        // key = role，如 "系统""/"ChatGPT、value = 收到的消息总数（显示大红点）
        private readonly Dictionary<string, int> _badgeCounts = new();
        private string _activeTab = "全部"; // 当前选中的标签
        private bool _isSidebarExpanded = true;

        // --- 发送历史记录管理 ---
        private readonly List<string> _history = new();
        private int _historyIndex = -1;
        private string _draftCurrent = string.Empty;

        public CentralCommandPanel()
        {
            InitializeComponent();
            RefreshRoleTabs(); // 初始化加载默认“全部”标签
            InitializeWebViewAsync();
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WebViewHub_CentralCMD"));
                await ResponseBoardWebView.EnsureCoreWebView2Async(env);
                
                string htmlTemplate = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body { font-family: -apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif,'Apple Color Emoji','Segoe UI Emoji'; color: #24292f; margin: 0; padding: 6px; background: #fdfdfd; overflow-y: auto; overflow-x: hidden; }
        ::-webkit-scrollbar { width: 8px; height: 8px; }
        ::-webkit-scrollbar-thumb { background: #d1d1d6; border-radius: 4px; }
        ::-webkit-scrollbar-track { background: transparent; }
        .card { margin: 0 0 10px 0; border: 1px solid #e5e5e7; border-radius: 8px; background: #fff; display: flex; flex-direction: column; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,0.04); }
        details { display: block; }
        summary { padding: 8px 12px; background: #f0f7ff; cursor: pointer; display: flex; align-items: center; font-size: 13px; font-weight: 600; color: #0284c7; outline: none; }
        summary:hover { background: #e0f0ff; }
        .time { margin-left: auto; color: #94a3b8; font-weight: normal; font-size: 11px; }
        .content { padding: 15px; border-top: 1px solid #e5e5e7; font-size: 14px; line-height: 1.6; word-wrap: break-word; overflow-x: hidden; }
        .content pre { background: #f6f8fa; padding: 16px; border-radius: 6px; overflow: auto; font-family: ui-monospace,SFMono-Regular,SF Mono,Menlo,Consolas,Liberation Mono,monospace; }
        .content code { background: rgba(175, 184, 193, 0.2); border-radius: 4px; padding: 0.2em 0.4em; font-family: inherit; font-size: 85%; }
        .content pre code { background: transparent; padding: 0; }
        .content table { border-collapse: collapse; width: 100%; margin: 16px 0; font-size: 14px; display: block; overflow-x: auto; white-space: nowrap; }
        .content table th { background-color: #f6f8fa; font-weight: 600; border: 1px solid #d0d7de; padding: 12px 18px; text-align: left; }
        .content table td { border: 1px solid #d0d7de; padding: 10px 18px; line-height: 1.6; }
        .content table tr:nth-child(2n) { background-color: #f6f8fa; }
        .content img { max-width: 100%; box-sizing: content-box; }
        .content blockquote { padding: 0 1em; color: #656d76; border-left: 0.25em solid #d0d7de; margin: 0; }
        .content a { color: #0969da; text-decoration: none; }
        .content a:hover { text-decoration: underline; }
    </style>
</head>
<body>
    <div id='board'></div>
    <script>
        function renderCards(jsonString) {
            try {
                let html = '';
                const cards = JSON.parse(jsonString);
                for(const c of cards) {
                    html += `<div class='card'>
                        <details open>
                            <summary>
                                <span style='margin-right:6px'>●</span>
                                ${c.role}
                                <span class='time'>${c.time}</span>
                            </summary>
                            <div class='content markdown-body'>${c.htmlContent}</div>
                        </details>
                    </div>`;
                }
                const board = document.getElementById('board');
                
                // 判断是否在底部
                const isScrolledToBottom = (window.innerHeight + window.scrollY) >= document.body.offsetHeight - 50;
                board.innerHTML = html || '<div style=""text-align:center;color:#94a3b8;margin-top:20px;font-size:12px"">暂无回答记录</div>';
                
                if(isScrolledToBottom) {
                    window.scrollTo(0, document.body.scrollHeight);
                }
            } catch(e) { console.error(e); }
        }
    </script>
</body>
</html>";
                ResponseBoardWebView.NavigateToString(htmlTemplate);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebUI核心初始化失败: {ex.Message}");
            }
        }

        private async void AddResponse(string role, string content)
        {
            content = FixMarkdownTableFormat(content);
            
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            string htmlContent = Markdown.ToHtml(content, pipeline);

            var existing = _responses.FirstOrDefault(r => r.Role == role);
            if (existing != null)
            {
                existing.HtmlContent = htmlContent;
                existing.Time = DateTime.Now.ToString("HH:mm:ss");
            }
            else
            {
                _responses.Add(new ResponseItem
                {
                    Role = role,
                    HtmlContent = htmlContent,
                    Time = DateTime.Now.ToString("HH:mm:ss")
                });
            }

            // 更新角标计数
            if (!_badgeCounts.ContainsKey(role)) _badgeCounts[role] = 0;
            _badgeCounts[role]++;

            // 刷新标签栏（角标变化）
            RefreshRoleTabs();
            
            await SyncToWebViewAsync();
        }

        private async void RoleTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string role)
            {
                _activeTab = role;
                // 点击标签后清零该 role 的角标
                if (role == "全部")
                    _badgeCounts.Clear();
                else
                    _badgeCounts[role] = 0;

                RefreshRoleTabs();
                await SyncToWebViewAsync();
            }
        }

        private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarExpanded = !_isSidebarExpanded;

            if (_isSidebarExpanded)
            {
                SidebarContainer.Width = 100;
                SidebarTitle.Visibility = Visibility.Visible;
                ToggleSidebarButton.ToolTip = "折叠通知栏";
            }
            else
            {
                SidebarContainer.Width = 46; // 折叠宽度刚好容纳按钮和角标
                SidebarTitle.Visibility = Visibility.Collapsed;
                ToggleSidebarButton.ToolTip = "展开通知栏";
            }

            // 重新刷新标签栏列表使其适应大小
            RefreshRoleTabs();
        }

        /// <summary>
        /// 动态生成标签栏：「全部」 + 每个 role 一个标签，含未读角标和选中高亮
        /// </summary>
        private void RefreshRoleTabs()
        {
            RoleTabsPanel.Children.Clear();

            // 所有节点：「全部」 + 所有出现过的 role
            var tabs = new List<string> { "全部" };
            tabs.AddRange(_responses.Select(r => r.Role).Distinct());

            foreach (var tab in tabs)
            {
                bool isActive = tab == _activeTab;
                int badge = tab == "全部" ? _badgeCounts.Values.Sum() : (_badgeCounts.TryGetValue(tab, out int v) ? v : 0);

                // 是否折叠
                string displayText = _isSidebarExpanded ? tab : (tab.Length > 0 ? tab.Substring(0, 1) : " ");

                // 外层容器（指示器 + 角标布局），底部留边距
                var container = new Grid { Margin = new Thickness(0, 0, 0, _isSidebarExpanded ? 6 : 8) };

                // 主标签按钮
                var btn = new Button
                {
                    Content = displayText,
                    Padding = _isSidebarExpanded ? new Thickness(10, 4, 10, 4) : new Thickness(0, 6, 0, 6),
                    FontSize = _isSidebarExpanded ? 12 : 14,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isActive ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) :  new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                    Background = isActive ? new SolidColorBrush(Color.FromRgb(240, 247, 255)) : Brushes.Transparent,
                    BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(186, 224, 255)) : new SolidColorBrush(Color.FromRgb(229, 229, 231)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = !_isSidebarExpanded ? tab : null, // 折叠时显示完整名字 tip
                    Tag = tab
                };
                btn.Resources.Add(typeof(Border), new Style(typeof(Border)) { Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(_isSidebarExpanded ? 14 : 6)) } });
                btn.Click += RoleTab_Click;
                container.Children.Add(btn);

                // 角标（消息数 > 0 时显示）
                if (badge > 0)
                {
                    var badgeEl = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                        CornerRadius = new CornerRadius(8),
                        MinWidth = 16,
                        Height = 16,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = _isSidebarExpanded ? new Thickness(0, -4, -4, 0) : new Thickness(0, -6, -4, 0),
                        Padding = new Thickness(3, 0, 3, 0),
                        IsHitTestVisible = false
                    };
                    badgeEl.Child = new TextBlock
                    {
                        Text = badge > 99 ? "99+" : badge.ToString(),
                        Foreground = Brushes.White,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    container.Children.Add(badgeEl);
                }

                RoleTabsPanel.Children.Add(container);
            }
        }



        private string FixMarkdownTableFormat(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return markdown;
            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
            
            // 第一遍寻找并剥离出头尾粘连的文字
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                
                // 剥离前面粘连的文字: "干扰文字|A|B|"
                if (line.Contains("|") && !line.StartsWith("|"))
                {
                    int firstPipe = line.IndexOf('|');
                    if (firstPipe > 0 && line.Count(c => c == '|') >= 2)
                    {
                        string preText = line.Substring(0, firstPipe).Trim();
                        string restText = line.Substring(firstPipe).Trim();
                        if (!string.IsNullOrEmpty(preText))
                        {
                            lines[i] = preText;
                            lines.Insert(i + 1, restText);
                            continue; // 刚折开的一行，让下个循环继续检视
                        }
                    }
                }

                // 剥离后面粘连的文字: "|A|B|干扰文字"
                if (line.StartsWith("|") && !line.EndsWith("|") && line.Count(c => c == '|') >= 2)
                {
                    int lastPipe = line.LastIndexOf('|');
                    if (lastPipe < line.Length - 1 && lastPipe > 0)
                    {
                        string validTablePart = line.Substring(0, lastPipe + 1).Trim();
                        string dirtyTailPart = line.Substring(lastPipe + 1).Trim();
                        if (!string.IsNullOrEmpty(dirtyTailPart))
                        {
                            lines[i] = validTablePart;
                            lines.Insert(i + 1, dirtyTailPart);
                            line = validTablePart;
                        }
                    }
                }
            }

            // 第二遍遍历：在干净的 |...| 行上方/下方强制塞入空行和表头分割带
            bool inTable = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("|") && line.EndsWith("|") && line.Length > 1)
                {
                    if (!inTable)
                    {
                        inTable = true;
                        // 1. 强制在表格上方补充空行 (CommonMark 铁律要求)
                        if (i > 0 && !string.IsNullOrWhiteSpace(lines[i - 1]))
                        {
                            lines.Insert(i, "");
                            i++; 
                        }

                        // 2. 检查表头下方是否缺少分割线
                        if (i + 1 < lines.Count)
                        {
                            var nextLine = lines[i + 1].Trim();
                            if (!nextLine.StartsWith("|") || (!nextLine.Contains("-") && !nextLine.Contains(":")))
                            {
                                int colCount = line.Count(c => c == '|') - 1;
                                if (colCount > 0)
                                {
                                    string separator = "|" + string.Join("|", Enumerable.Repeat("---", colCount)) + "|";
                                    lines.Insert(i + 1, separator);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // 出了表格范围了，如果有表格没封闭（比如没空行），补空行打断
                    if (inTable && !string.IsNullOrWhiteSpace(line))
                    {
                        inTable = false;
                        lines.Insert(i, "");
                        i++;
                    }
                    else if (string.IsNullOrWhiteSpace(line))
                    {
                        inTable = false;
                    }
                }
            }
            return string.Join("\n", lines);
        }

        private async Task SyncToWebViewAsync()
        {
            if (ResponseBoardWebView.CoreWebView2 != null)
            {
                // 按当前选中标签过滤显示内容
                var toShow = _activeTab == "全部"
                    ? _responses.ToList()
                    : _responses.Where(r => r.Role == _activeTab).ToList();

                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                string json = JsonSerializer.Serialize(toShow, options);
                string encodedJson = JsonSerializer.Serialize(json);
                await ResponseBoardWebView.ExecuteScriptAsync($"renderCards({encodedJson});");
            }
        }

        private void ClearResponsesButton_Click(object sender, RoutedEventArgs e)
        {
            _responses.Clear();
            _badgeCounts.Clear();
            _activeTab = "全部";
            RefreshRoleTabs();
            _ = SyncToWebViewAsync();
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            string fullText = new TextRange(CommandInput.Document.ContentStart, CommandInput.Document.ContentEnd).Text.Trim();
            if (string.IsNullOrEmpty(fullText))
                return;

            if (MainWindowReference == null)
            {
                MessageBox.Show("未设定 MainWindow 引用，无法分发指令。");
                return;
            }

            var webViews = MainWindowReference.GetAllWebViews();
            if (webViews == null || webViews.Count == 0)
            {
                MessageBox.Show("当前没有开启任何 AI 网页。");
                return;
            }
            
            // 记录历史（不重复记录连续一样的指令）
            if (_history.Count == 0 || _history.Last() != fullText)
            {
                _history.Add(fullText);
            }
            _historyIndex = _history.Count; // 指向最新之后
            _draftCurrent = string.Empty;

            // 支持换行切分指令，每行如果以 @ 开头，就算一个新指令段
            // 采用 Multiline 使得 ^ 匹配每行的开头，Singleline 使得 . 包括换行符
            var sectionMatches = Regex.Matches(fullText, @"^@(\w+)\s+(.*?)(?=(^@|\z))", RegexOptions.Singleline | RegexOptions.Multiline);

            if (sectionMatches.Count == 0)
            {
                AddResponse("系统", "未找到有效的目标角色指令。请在行首使用: @角色名 内容");
                return;
            }

            foreach (Match sec in sectionMatches)
            {
                string targetRole = sec.Groups[1].Value.Trim();
                string commandBody = sec.Groups[2].Value.Trim();

                if (string.IsNullOrEmpty(commandBody)) continue;

                var targetViews = webViews.Where(v => string.Equals(v.RoleTag, targetRole, StringComparison.OrdinalIgnoreCase)).ToList();

                if (targetViews.Count == 0)
                {
                    AddResponse("系统", $"未找到匹配角色标签 [{targetRole}] 的窗口，已跳过。");
                    continue;
                }

                // --- 跨 AI 上下文交互逻辑（内置 @引用）---
                var embeddedRoleMatches = Regex.Matches(commandBody, @"@(\w+)");

                foreach (Match embeddedMatch in embeddedRoleMatches)
                {
                    string sourceRole = embeddedMatch.Groups[1].Value;
                    var sourceView = webViews.FirstOrDefault(v => string.Equals(v.RoleTag, sourceRole, StringComparison.OrdinalIgnoreCase));

                    if (sourceView != null && !string.Equals(sourceRole, targetRole, StringComparison.OrdinalIgnoreCase))
                    {
                        AddResponse("系统", $"👉 正在从 [{sourceRole}] 抓取上下文给 [{targetRole}]...");
                        string lastReply = await sourceView.FetchLastResponseAsync();
                        if (!string.IsNullOrEmpty(lastReply))
                        {
                            string replacement = $"\n\n【以下是来自 {sourceRole} 的内容】:\n{lastReply}\n\n";
                            commandBody = commandBody.Replace(embeddedMatch.Value, replacement);
                        }
                        else
                        {
                            commandBody = commandBody.Replace(embeddedMatch.Value, $"【尝试提取 {sourceRole} 失败】");
                        }
                    }
                }

                foreach (var view in targetViews)
                {
                    AddResponse(targetRole, $"👉 已分发指令，等待 {targetRole} 回答中...");
                    await view.InjectAndSendAsync(commandBody);
                    // 启动一个后台独立监控任务，用于在 AI 回答结束后自动抓取答案回显
                    _ = StartAutoFetchResponseTask(view, targetRole);
                }
            }
            
            // 成功分发后清空富文本输入框
            CommandInput.Document.Blocks.Clear();
            CommandInput.Document.Blocks.Add(new Paragraph());
        }

        private async Task StartAutoFetchResponseTask(WebViewContainer view, string targetRole)
        {
            try
            {
                // ── 阶段一：等待 AI 完成输出 ──
                // 通过 JS 轮询各平台的"停止生成"指示器，消失则表示完成
                // 先等 2 秒让输出开始（避免一抓就是旧内容）
                await Task.Delay(2000);

                // JS 脚本：检查页面上是否还有停止生成的按钮，有则 AI 还在输出
                string waitScript = @"
                    (function() {
                        if (document.querySelector('[data-testid=""stop-button""], button[aria-label=""Stop generating""]'))
                            return 'streaming';
                        if (document.querySelector('button[aria-label=""Stop response""], .stop-button, [data-test-id=""stop-button""]'))
                            return 'streaming';
                        if (document.querySelector('button[data-testid=""stop_response""]'))
                            return 'streaming';
                        if (document.querySelector('button[aria-label=""Stop""]'))
                            return 'streaming';
                        if (document.querySelector('[class*=""stop""][class*=""btn""], button[class*=""stop""]'))
                            return 'streaming';
                        var stopEl = Array.from(document.querySelectorAll('button')).find(function(b) {
                            return /stop|\u505c\u6b62|\u4e2d\u65ad|cancel/i.test(b.textContent + b.ariaLabel) && b.offsetHeight > 0;
                        });
                        return stopEl ? 'streaming' : 'done';
                    })();
                ";

                // 最多等 90 秒，每 1.5 秒检查一次
                int maxWait = 60;
                for (int i = 0; i < maxWait; i++)
                {
                    await Task.Delay(1500);
                    var coreWebView = view.GetCoreWebView2();
                    if (coreWebView == null) break;

                    string statusRaw = await coreWebView.ExecuteScriptAsync(waitScript);
                    string status = statusRaw?.Trim('"') ?? "done";
                    if (status == "done") break;
                }

                // 额外缓冲，确保 DOM 稳定
                await Task.Delay(500);

                // ── 阶段二：一次性抓取最终回复 ──
                string finalReply = await view.FetchLastResponseAsync();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(finalReply))
                        AddResponse(targetRole, $"✅ 收到了来自 {targetRole} 的回复：\n" + finalReply);
                    else
                        AddResponse(targetRole, $"⚠ 等待 {targetRole} 的回复超时或未获取到内容。");
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AddResponse(targetRole, $"❌ 监听提取异常：{ex.Message}");
                });
            }
        }

        private async void FetchButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindowReference == null) return;

            var webViews = MainWindowReference.GetAllWebViews().Where(v => !string.IsNullOrEmpty(v.RoleTag)).ToList();

            if (webViews.Count == 0)
            {
                AddResponse("系统", "没有找到配置了角色标签的窗口。");
                return;
            }

            FetchButton.IsEnabled = false;

            // 并发抓取所有 AI 的回复
            var tasks = webViews.Select(async view =>
            {
                try
                {
                    string reply = await view.FetchLastResponseAsync();
                    AddResponse(
                        view.RoleTag,
                        string.IsNullOrEmpty(reply) ? "⚠ 未抓取到有效回复（AI 可能还在思考）" : reply
                    );
                }
                catch (Exception ex)
                {
                    AddResponse(view.RoleTag, $"❌ 抓取失败: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks);
            FetchButton.IsEnabled = true;
        }

        #region @自动补全逻辑与富文本格式化

        private void CommandInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInsertingTag || MainWindowReference == null) return;

            var caret = CommandInput.CaretPosition;
            if (caret == null) return;

            // 往回读取同一 Run 里的内容，检测输入的 @
            string textBeforeCaret = caret.GetTextInRun(LogicalDirection.Backward);
            if (string.IsNullOrEmpty(textBeforeCaret))
            {
                RoleTagPopup.IsOpen = false;
                return;
            }

            int lastAt = textBeforeCaret.LastIndexOf('@');
            if (lastAt >= 0)
            {
                string typed = textBeforeCaret.Substring(lastAt + 1);
                // 确保触发时中途没有换行或空格
                if (!typed.Contains(" ") && !typed.Contains("\n") && !typed.Contains("\r"))
                {
                    var webViews = MainWindowReference.GetAllWebViews();
                    var tags = webViews.Select(v => v.RoleTag)
                                       .Where(t => !string.IsNullOrEmpty(t) && t.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                                       .Distinct()
                                       .ToList();
                    
                    if (tags.Count > 0)
                    {
                        RoleTagListBox.ItemsSource = tags;
                        RoleTagListBox.SelectedIndex = 0;
                        
                        // 让 Popup 跟随光标位置
                        var rect = caret.GetCharacterRect(LogicalDirection.Backward);
                        RoleTagPopup.PlacementRectangle = rect;
                        RoleTagPopup.IsOpen = true;
                        return;
                    }
                }
            }

            RoleTagPopup.IsOpen = false;
        }

        private void CommandInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (RoleTagPopup.IsOpen)
            {
                if (e.Key == Key.Down)
                {
                    if (RoleTagListBox.SelectedIndex < RoleTagListBox.Items.Count - 1)
                        RoleTagListBox.SelectedIndex++;
                    e.Handled = true;
                }
                else if (e.Key == Key.Up)
                {
                    if (RoleTagListBox.SelectedIndex > 0)
                        RoleTagListBox.SelectedIndex--;
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter || e.Key == Key.Tab)
                {
                    InsertSelectedTag();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    RoleTagPopup.IsOpen = false;
                    e.Handled = true;
                }
            }
            else
            {
                // 无弹窗时，只依靠 Enter 派发；Shift+Enter 实现换行输入。
                if (e.Key == Key.Enter)
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        // 放行，让 RichTextBox 自己处理换行
                        return;
                    }
                    else
                    {
                        e.Handled = true;
                        SendButton_Click(this, new RoutedEventArgs());
                    }
                }
                else if (e.Key == Key.Up)
                {
                    e.Handled = NavigateHistory(-1);
                }
                else if (e.Key == Key.Down)
                {
                    e.Handled = NavigateHistory(1);
                }
                else if (e.Key == Key.Left)
                {
                    e.Handled = JumpOverTagRun(LogicalDirection.Backward);
                }
                else if (e.Key == Key.Right)
                {
                    e.Handled = JumpOverTagRun(LogicalDirection.Forward);
                }
                else if (e.Key == Key.Back)
                {
                    // Backspace：如果光标前面紧贴着蓝色 @标签，整体删除该标签
                    e.Handled = DeleteTagRun();
                }
            }
        }

        private void RoleTagListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (RoleTagListBox.SelectedItem != null)
            {
                InsertSelectedTag();
            }
        }

        private void InsertSelectedTag()
        {
            if (RoleTagListBox.SelectedItem is string tag)
            {
                _isInsertingTag = true;
                try
                {
                    var caret = CommandInput.CaretPosition;
                    string textBeforeCaret = caret.GetTextInRun(LogicalDirection.Backward);
                    if (string.IsNullOrEmpty(textBeforeCaret)) return;

                    int lastAt = textBeforeCaret.LastIndexOf('@');
                    if (lastAt >= 0)
                    {
                        int charsToDelete = textBeforeCaret.Length - lastAt;
                        var startPos = caret.GetPositionAtOffset(-charsToDelete, LogicalDirection.Backward);
                        
                        if (startPos != null)
                        {
                            // 清除刚才敲入的带 @ 的半成品长字符
                            var rangeToDelete = new TextRange(startPos, caret);
                            rangeToDelete.Text = "";

                            // 插入带有蓝色高亮和加粗格式的成品标签，例如 "@gemini"
                            var tagRange = new TextRange(startPos, startPos);
                            tagRange.Text = "@" + tag;
                            tagRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(2, 132, 199)));
                            tagRange.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                            
                            // 更新光标到高亮区块之后
                            CommandInput.CaretPosition = tagRange.End;
                            
                            // 插入跟随的黑色空格，恢复正常输入样式
                            var spaceRange = new TextRange(CommandInput.CaretPosition, CommandInput.CaretPosition);
                            spaceRange.Text = " ";
                            spaceRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(51, 51, 51)));
                            spaceRange.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                            
                            CommandInput.CaretPosition = spaceRange.End;
                        }
                    }
                }
                finally
                {
                    RoleTagPopup.IsOpen = false;
                    _isInsertingTag = false;
                    CommandInput.Focus();
                }
            }
        }

        private bool NavigateHistory(int direction)
        {
            if (_history.Count == 0) return false;

            // 如果刚开始翻历史，保存当前打了一半的草稿
            if (_historyIndex == _history.Count)
            {
                _draftCurrent = new TextRange(CommandInput.Document.ContentStart, CommandInput.Document.ContentEnd).Text.TrimEnd();
            }

            int nextIndex = _historyIndex + direction;
            if (nextIndex < 0 || nextIndex > _history.Count) return false;

            _historyIndex = nextIndex;
            string textToSet = _historyIndex == _history.Count ? _draftCurrent : _history[_historyIndex];

            // 恢复内容到 RichTextBox，这里可以粗略使用纯文本，如果用户要重发带颜色的 @ 也无妨（因为上面正则支持纯文本匹配 @）
            CommandInput.Document.Blocks.Clear();
            CommandInput.Document.Blocks.Add(new Paragraph(new Run(textToSet)));
            CommandInput.CaretPosition = CommandInput.Document.ContentEnd;
            return true;
        }

        private bool JumpOverTagRun(LogicalDirection direction)
        {
            var caret = CommandInput.CaretPosition;
            if (caret == null) return false;

            // 尝试获取接下来将要移动到的邻接 TextPointer
            var nextPos = caret.GetNextInsertionPosition(direction);
            if (nextPos == null) return false;

            // 获取该位置所在的段内对象
            var run = nextPos.Parent as Run;
            if (run != null)
            {
                // 检测是否是我们之前标记的标签的特点（蓝字，加粗）
                if (run.FontWeight == FontWeights.Bold && run.Foreground is SolidColorBrush brush && brush.Color == Color.FromRgb(2, 132, 199))
                {
                    // 把光标直接甩过这个标签 Run
                    CommandInput.CaretPosition = direction == LogicalDirection.Forward ? run.ElementEnd : run.ElementStart;
                    return true;
                }
            }

            return false;
        }

        private bool DeleteTagRun()
        {
            var caret = CommandInput.CaretPosition;
            if (caret == null) return false;

            // 向后（Backward = 左边）探测邻接位置所在的 Run
            var prevPos = caret.GetNextInsertionPosition(LogicalDirection.Backward);
            if (prevPos == null) return false;

            var run = prevPos.Parent as Run;
            if (run != null)
            {
                // 判断是否是蓝色加粗的 @标签（与插入时的格式相同）
                if (run.FontWeight == FontWeights.Bold &&
                    run.Foreground is SolidColorBrush brush &&
                    brush.Color == Color.FromRgb(2, 132, 199))
                {
                    // 选中整个 Run 并删除
                    var range = new TextRange(run.ElementStart, run.ElementEnd);
                    range.Text = string.Empty;
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
