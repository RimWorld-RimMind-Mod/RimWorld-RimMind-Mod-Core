#!/usr/bin/env python3
"""
RimWorld Mod Release Analyzer
分析两个 tag 之间的所有 commits 和 PRs，调用 AI 生成 Release Notes
"""

import argparse
import json
import os
import sys
import time
from openai import OpenAI, APIError, APIStatusError, AuthenticationError

def build_prompt(tag_name: str, prs: list, commits: str) -> str:
    """构建发送给 AI 的 prompt"""
    prs_text = []
    for pr in prs:
        prs_text.append(f"""
PR #{pr.get('number', 'N/A')}:
- 标题: {pr.get('title', 'N/A')}
- 作者: @{pr.get('author', 'N/A')}
- 标签: {', '.join(pr.get('labels', [])) or '无'}
- 描述: {(pr.get('body') or '无描述')[:500]}
""")
    prs_section = "\n".join(prs_text) if prs_text else "无 PR 数据"
    commits_section = commits if commits else "无 Commit 数据"

    prompt = f"""你是代码发布管理员。请分析以下自上个版本以来的 PR 和 Commit 数据，为版本 {tag_name} 生成专业的发布说明（Release Notes）。

## 本次发布的版本号
{tag_name}

## 原始 Commits
```text
{commits_section}
```

## 合并的 Pull Requests
{prs_section}

## 输出要求
1. 请分别生成中文和英文的发布说明，中文在前，英文在后。
2. 按照常见分类（如：新特性、Feature、Bug 修复、Fix、优化、Improvement、文档、Documentation）进行组织。
3. 语气专业、简明扼要。如果 PR 包含标签，请结合标签推断改动性质。
4. 直接输出 Markdown 格式的发布说明，不要使用任何代码块标记（如 ```json 或 ```markdown），不要包含任何解释性文字。

格式参考：
## 发布说明

### 🌟 新特性
- 实现了情绪记忆系统

### 🐛 Bug 修复
- 修复了导致崩溃的寻路问题

---

## Release Notes

### 🌟 Features
- Implemented emotion memory system

### 🐛 Fixes
- Fixed a crash related to pathfinding
"""
    return prompt.strip()

def call_deepseek(prompt: str) -> tuple:
    """调用 DeepSeek API，带重试机制"""
    api_key = os.environ.get("DEEPSEEK_API_KEY")
    if not api_key:
        raise AuthenticationError("DEEPSEEK_API_KEY 环境变量未设置")

    base_url = os.environ.get("DEEPSEEK_BASE_URL", "https://api.deepseek.com")
    model = os.environ.get("DEEPSEEK_MODEL", "deepseek-chat")
    client = OpenAI(api_key=api_key, base_url=base_url, timeout=60)

    max_retries = 3
    for attempt in range(max_retries):
        try:
            print(f"::debug::正在调用 AI 生成说明 (尝试 {attempt + 1}/{max_retries})...")
            response = client.chat.completions.create(
                model=model,
                messages=[
                    {"role": "system", "content": "你是资深的开源项目维护者，负责编写清晰易读的 Release Notes。请直接输出 Markdown 格式的发布说明，不要包含任何代码块标记。"},
                    {"role": "user", "content": prompt}
                ],
                temperature=0.3,
                max_tokens=4096,
            )
            return response.choices[0].message.content.strip()
        except AuthenticationError as e:
            print(f"::error::API 认证失败: {e}")
            raise
        except (APIError, APIStatusError) as e:
            print(f"::warning::API 错误: {e}")
            if attempt < max_retries - 1:
                time.sleep(2 ** attempt)
            else:
                raise
        except Exception as e:
            print(f"::warning::未知错误: {e}")
            raise

def generate_fallback_notes(prs: list, commits: str) -> tuple:
    """API 失败时的备用生成逻辑"""
    lines = [f"- {pr.get('title', '无标题')}" for pr in prs]
    if not lines:
        lines = ["- 常规代码更新与维护"]
    
    notes_zh = "### 变更内容\n" + "\n".join(lines)
    notes_en = "### Changes\n" + "\n".join(lines)
    return notes_zh, notes_en

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tag", required=True, help="当前版本 Tag")
    parser.add_argument("--prs-file", required=True, help="PR JSON 数据路径")
    parser.add_argument("--commits-txt", required=True, help="Commit TXT 数据路径")
    args = parser.parse_args()

    # 1. 加载数据
    with open(args.prs_file, "r", encoding="utf-8") as f:
        prs = json.load(f)
    with open(args.commits_txt, "r", encoding="utf-8") as f:
        commits_content = f.read()

    print("::group::[Debug] AI 输入数据核查")
    print(f"目标 Tag: {args.tag}")
    print(f"提取到 PR 数量: {len(prs)}")
    print(f"Commit 字符总数: {len(commits_content)}")
    print("::endgroup::")

    if not prs and not commits_content.strip():
        print("::warning::没有发现任何 PR 或 Commit，生成默认说明。")
        notes_zh, notes_en = generate_fallback_notes(prs, commits_content)
        combined_notes = f"""## 发布说明

{notes_zh}

---

## Release Notes

{notes_en}
"""
    else:
        prompt = build_prompt(args.tag, prs, commits_content)
        try:
            notes_content = call_deepseek(prompt)
            combined_notes = notes_content
        except Exception as e:
            print(f"::error::AI 调用最终失败，使用备用方案生成。错误: {e}")
            notes_zh, notes_en = generate_fallback_notes(prs, commits_content)
            combined_notes = f"""## 发布说明

{notes_zh}

---

## Release Notes

{notes_en}
"""

    print("::group::[Debug] 生成的最终 Release Note 预览")
    print(combined_notes)
    print("::endgroup::")

    with open("/tmp/release_notes.md", "w", encoding="utf-8") as f:
        f.write(combined_notes)

if __name__ == "__main__":
    main()