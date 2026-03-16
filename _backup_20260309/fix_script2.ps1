$content = Get-Content 'e:\ProjectMy\WebViewHub\Controls\CentralCommandPanel.xaml.cs' -Raw -Encoding UTF8

$oldQwen = @'
                // 2. 针对特定平台的特征类名检测
                // Qwen (千问): 找到最新的消息，检查是否完成
                var qwenMessages = document.querySelectorAll('.qk-markdown-complete, .qk-markdown');
                if (qwenMessages.length > 0) {
                    // 有完成的消息，检查最新的一个是否有完成标记
                    var latestQwen = qwenMessages[qwenMessages.length - 1];
                    var isComplete = latestQwen.classList.contains('qk-markdown-complete');
                    // 如果没有完成标记，说明还在生成中
                    if (!isComplete) {
                        return 'streaming';
                    }
                } else {
                    // 没有找到任何消息，检查是否有正在生成的消息
                    var qk = document.querySelector('.qk-markdown');
                    if (qk) {
                        return 'streaming';
                    }
                }
'@

$newQwen = @'
                // 2. Qwen (千问)
                if (host.includes('qianwen') || host.includes('tongyi')) {
                    var qwenMessages = document.querySelectorAll('.qk-markdown-complete, .qk-markdown');
                    if (qwenMessages.length > 0) {
                        var latestQwen = qwenMessages[qwenMessages.length - 1];
                        if (!latestQwen.classList.contains('qk-markdown-complete')) { return 'streaming'; }
                    } else if (document.querySelector('.qk-markdown')) { return 'streaming'; }
                }

                // 3. 豆包 (doubao)
                if (host.includes('doubao.com') || host.includes('doubao')) {
                    var thinking = document.querySelector('[class*="thinking"], [class*="generating"], [class*="loading"], [class*="streaming"]');
                    if (thinking && thinking.offsetParent !== null) { return 'streaming'; }
                }

                // 4. Grok
                if (host.includes('grok.com') || host.includes('x.ai')) {
                    var grokLoading = document.querySelector('[class*="generating"], [class*="streaming"], [class*="loading"], [class*="thinking"]');
                    if (grokLoading && grokLoading.offsetParent !== null) { return 'streaming'; }
                }
'@

$content = $content -replace [regex]::Escape($oldQwen), $newQwen
[System.IO.File]::WriteAllText('e:\ProjectMy\WebViewHub\Controls\CentralCommandPanel.xaml.cs', $content, [System.Text.Encoding]::UTF8)
Write-Host "Done - added Doubao and Grok detection"
