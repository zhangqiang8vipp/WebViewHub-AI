$content = Get-Content 'e:\ProjectMy\WebViewHub\Controls\CentralCommandPanel.xaml.cs' -Raw -Encoding UTF8
$oldScript = @'
                    (function() {
                        // 1. 通用"停止"按钮检测（包含主流平台）
'@
$newScript = @'
                    (function() {
                        var host = location.hostname;
                        // 1. 通用"停止"按钮检测（包含主流平台）
'@
$content = $content -replace [regex]::Escape($oldScript), $newScript
[System.IO.File]::WriteAllText('e:\ProjectMy\WebViewHub\Controls\CentralCommandPanel.xaml.cs', $content, [System.Text.Encoding]::UTF8)
Write-Host "Done"
