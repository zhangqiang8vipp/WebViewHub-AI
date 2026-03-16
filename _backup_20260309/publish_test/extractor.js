(function () {
    function logError(msg, err) {
        return "Error: " + msg + " | " + (err ? err.message + "\n" + err.stack : "");
    }

    try {
        let host = location.hostname;

        // 1. 获取所有相关的消息节点
        let nodes = document.querySelectorAll(`
            model-response,
            .markdown-body,
            .markdown,
            .prose,
            [data-message-author-role="assistant"],
            [class*="AssistantMessage"],
            [class*="message"][class*="assistant"],
            [data-testid="receive_message"],
            [data-testid*="message-item"],
            [class*="chat"][class*="item"],
            [class*="message-item"],
            [class*="message-block"],
            [class*="bubble--ai"],
            .response-content,
            .message-content,
            .ai-message,
            .ds-markdown,
            .ds-markdown-body,
            [class*="ds-message-bubble"],
            [class*="deepseek-chat"],
            .qk-markdown,
            .qk-markdown-complete,
            .qk-md-paragraph,
            .qk-md-text,
            .markdown-pc-special-class,
            #qk-markdown-react,
            .msg-content,
            .chat-msg-content,
            [class*="assistant"]
        `);

        // 2. 过滤无用节点（包括字数极短的组件如 1.5s Fast 等，以及 User 自己发的信息）
        let validNodes = Array.from(nodes).filter(n => {
            let cl = (n.className || "").toString().toLowerCase();
            let role = (n.getAttribute("data-message-author-role") || "").toLowerCase();
            if (role === "user" || cl.includes("user") || cl.includes("human") || cl.includes("prompt")) return false;

            let text = n.innerText || n.textContent || "";
            
            // 千问的特殊容器：qk-markdown-complete 无论内容长短都要保留
            if (cl.includes('qk-markdown-complete') || cl.includes('markdown-pc-special-class')) {
                return true;
            }
            
            return text.trim().length > 15;
        });

        // 3. 剔除那些只是用来包裹整个对话列表的超大外壳
        // 做法：如果一个节点包含了其它有效的消息气泡，通常它是最外层的 Dialog/Thread 壳子。
        // 但是！如果这个节点本身就是已知的“正文容器”（如 .markdown, .qk-markdown），我们一定要保留它，而不是保留里面的原子段落。
        let finalNodes = [];

        // 定义已知的高优先级容器特征
        const containerTraits = ["markdown", "prose", "message-content", "response-content", "qk-markdown", "qk-markdown-complete", "qk-md-text", "qk-md-paragraph", "qk-markdown-react", "markdown-pc-special-class", "ds-markdown"];

        validNodes.forEach(n => {
            let cl = (n.className || "").toString().toLowerCase();
            let isKnownContainer = containerTraits.some(trait => cl.includes(trait));

            // 检查是否包含其它有效节点
            let containsOther = validNodes.some(other => {
                if (other === n) return false;
                // 如果对方是我的子孙，且我不属于已知优先级容器，则我可能只是个外壳，应该被剔除
                return n.contains(other);
            });

            // 策略：如果是已知容器，强制保留；否则，如果不包含其它候选节点，保留。
            if (isKnownContainer || !containsOther) {
                finalNodes.push(n);
            }
        });

        // 此时 validNodes 存放的是最符合条件的节点集群
        validNodes = finalNodes;

        // 4. 兜底法：如果是极端的不可识别 DOM，找复制按钮的爸爸
        if (validNodes.length === 0) {
            let copyBtns = Array.from(document.querySelectorAll('button[aria-label*="Copy" i], button[aria-label*="复制"], button[title*="Copy" i], button[title*="复制"], button.copy'))
                .filter(b => b.offsetHeight > 0 || b.offsetWidth > 0);

            if (copyBtns.length > 0) {
                let lastBtn = copyBtns[copyBtns.length - 1];
                let parent = lastBtn.closest('.markdown, .prose, article, [class*="message"], [class*="response"], [class*="item"]');
                if (!parent) parent = lastBtn.parentElement.parentElement;
                if (parent) validNodes = [parent];
            }
        }

        if (validNodes.length === 0) {
            return "Error: 找不到匹配的 AI 回复大纲节点且未探测到通用特征。 Host: " + host;
        }

        // 锁定最终外壳 - 优先找最新完成的千问消息
        let targetNode = validNodes[validNodes.length - 1];
        
        // 对于千问，尝试找到最新的 .qk-markdown-complete 消息
        if (host.includes('qianwen') || host.includes('tongyi')) {
            const qwenMessages = document.querySelectorAll('.qk-markdown-complete');
            if (qwenMessages.length > 0) {
                // 取最后一个（最新的）
                targetNode = qwenMessages[qwenMessages.length - 1];
            }
        }

        // （已删除：强行往下找子容器的代码。因为诸如 Gemini 经常把表格和后面的文字拆分为同一父级下的兄弟节点，
        // 比如上面是个表格容器下面是个 p 容器。如果深掏只取首个，后面就丢弃了！）

        if (targetNode && typeof targetNode.scrollIntoView === 'function') {
            try { targetNode.scrollIntoView({ block: "end", behavior: "instant" }); } catch (e) { }
        }

        // 5. 将正规军安全拉入沙箱（普通外壳必须原封不动放入，如果是 ShadowDOM 就要解构进去）
        const clone = document.createElement("div");
        if (targetNode.shadowRoot) {
            Array.from(targetNode.shadowRoot.childNodes).forEach(c => clone.appendChild(c.cloneNode(true)));
        } else {
            // 切忌用 clone.appendChild(c) 遍历 childNodes，会破坏掉 table 等依赖父级的容器！！！
            clone.appendChild(targetNode.cloneNode(true));
        }

        // 6. 剔除噪音（重新生成的小标、各种乱入的复制和朗读图标、甚至是下面跟随的“猜你想问”小提示）
        const trash = [
            "button", "svg", "img", ".copy", ".toolbar", ".actions", ".feedback", "[role='button']",
            "[class*='suggestion']", "[class*='recommend']", "[class*='related']", "[class*='hint']",
            "[class*='action-bar']", "[class*='bottom-bar']", "[class*='extra-info']", "[class*='suggest-question']",
            "[data-foundation-type*='suggest']", "[data-testid*='suggest']", "[class*='suggest-message']"
        ];
        trash.forEach(sel => {
            try { clone.querySelectorAll(sel).forEach(n => n.remove()); } catch (e) { }
        });

        // 6.1 (新增) 针对豆包等平台通过文本内容识别剔除“猜你想问”等板块
        try {
            const blockKeywords = ["猜你想问", "相关问题", "推荐提问", "大家都在搜"];
            const allElements = clone.querySelectorAll('div, span, p, h1, h2, h3, h4, h5');
            allElements.forEach(el => {
                if (blockKeywords.some(kw => (el.innerText || "").includes(kw))) {
                    // 如果匹配到关键词，向上找 3 层，如果是容器就干掉（通常推荐位是个 div 包裹一堆 button）
                    let p = el;
                    for (let i = 0; i < 3 && p; i++) {
                        if (p.parentElement && p.parentElement !== clone) {
                            p = p.parentElement;
                        } else {
                            break;
                        }
                    }
                    if (p && p !== clone) p.remove();
                }
            });
        } catch (e) { }

        // 7. 格式逆向修复流水线
        clone.querySelectorAll("pre").forEach(pre => {
            const code = pre.querySelector("code");
            if (!code) return;
            let lang = "";
            if (code.className) {
                const m = code.className.match(/language-(\w+)/);
                if (m) lang = m[1];
            }
            pre.setAttribute("data-lang", lang);
        });

        clone.querySelectorAll(".katex, .math").forEach(node => {
            const tex = node.innerText || node.textContent;
            let finalTex = (tex || "").split('\n').join(' ').trim();
            const text = document.createTextNode(` $${finalTex}$ `);
            node.replaceWith(text);
        });

        // ChatGPT 等标准表格修复（Turndown 不认没 thead 的表格会导致 fallback 为普通平铺纯文本）
        clone.querySelectorAll("table").forEach(table => {
            if (!table.querySelector("thead")) {
                const first = table.querySelector("tr");
                if (!first) return;
                const thead = document.createElement("thead");
                thead.appendChild(first.cloneNode(true));
                table.insertBefore(thead, table.firstChild);
            }
        });

        let Service = window.TurndownService || (typeof TurndownService !== 'undefined' ? TurndownService : null);
        if (!Service) return "Error: TurndownService 脚本加载域异常丢失。";

        const td = new Service({ codeBlockStyle: "fenced", headingStyle: "atx", hr: "---" });

        td.addRule("fencedCode", {
            filter: node => node.nodeName.toUpperCase() === "PRE",
            replacement: (content, node) => {
                const lang = node.getAttribute("data-lang") || "";
                const code = node.innerText || node.textContent || "";
                return `\n\`\`\`${lang}\n${code.replace(/```/g, '\\`\\`\\`')}\n\`\`\`\n`;
            }
        });

        td.addRule("tables", {
            // 确保强拦截 table 自身（之前可能因为闭包或选择器不精确被穿透了）
            filter: node => node.nodeName.toUpperCase() === "TABLE",
            replacement: function (content, node) {
                let md = "\n\n";
                const rows = Array.from(node.querySelectorAll("tr"));
                rows.forEach((tr, i) => {
                    const cells = Array.from(tr.querySelectorAll("th, tdd, td"));
                    // 因为 td 里可能有内部换行，会导致 Markdown 的 pipe format 被回车打断导致表格断裂，这必须用正则干掉
                    const row = cells.map(c => (c.innerText || c.textContent || "").trim().replace(/\r?\n/g, '<br/>'));
                    md += "| " + row.join(" | ") + " |\n";
                    if (i === 0) {
                        md += "| " + row.map(() => "---").join(" | ") + " |\n";
                    }
                });
                return md + "\n";
            }
        });

        // 8. 调用转化（以 DOM 参数突破 TrustedHTML 限制）
        let resultMd = td.turndown(clone);

        // 9. 降级安全带
        if ((!resultMd || resultMd.trim() === "") && (targetNode.innerText || targetNode.textContent)) {
            resultMd = targetNode.innerText || targetNode.textContent;
            if (resultMd.trim().length > 0) {
                resultMd += "\n\n*(注：因页面结构异常突变，此处回退由底层强制纯文本转储提取)*";
            }
        }

        return resultMd;

    } catch (e) {
        return logError("JS 提取逻辑在执行期间发生未处理崩溃", e);
    }
})();
