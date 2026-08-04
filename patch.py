import os
import re

files = [
    r"I:\BanglaHost\src\BanglaHost.App\Views\DatabasesPage.xaml.cs",
    r"I:\BanglaHost\src\BanglaHost.App\Views\SitesPage.xaml.cs",
    r"I:\BanglaHost\src\BanglaHost.App\Views\SiteListControl.xaml.cs",
    r"I:\BanglaHost\src\BanglaHost.App\Views\ServicesPage.xaml.cs",
    r"I:\BanglaHost\src\BanglaHost.App\Views\ExplorerPage.xaml.cs",
    r"I:\BanglaHost\src\BanglaHost.App\Views\FilesPage.xaml.cs"
]

def fix_async_void(content):
    pattern_expr = re.compile(r'(private|public|protected)\s+async\s+void\s+(\w+)\s*\(([^)]*)\)\s*=>\s*(.*?);', re.MULTILINE | re.DOTALL)
    def repl_expr(m):
        vis, name, args, body = m.groups()
        return f"{vis} async void {name}({args})\n    {{\n        try {{ {body}; }}\n        catch {{ }}\n    }}"
    
    content = pattern_expr.sub(repl_expr, content)

    lines = content.split('\n')
    out_lines = []
    i = 0
    while i < len(lines):
        line = lines[i]
        match = re.search(r'(private|public|protected)(\s+override)?\s+async\s+void\s+(\w+)\s*\(', line)
        if match and not '=>' in line:
            out_lines.append(line)
            i += 1
            while i < len(lines) and '{' not in lines[i]:
                out_lines.append(lines[i])
                i += 1
            
            if i < len(lines):
                brace_line = lines[i]
                j = i + 1
                has_try = False
                while j < len(lines):
                    l = lines[j].strip()
                    if not l:
                        j += 1
                        continue
                    if l.startswith('try ') or l == 'try' or l.startswith('try{'):
                        has_try = True
                    break
                
                if not has_try:
                    brace_depth = 0
                    start_j = i
                    end_j = -1
                    for k in range(i, len(lines)):
                        brace_depth += lines[k].count('{')
                        brace_depth -= lines[k].count('}')
                        if brace_depth == 0:
                            end_j = k
                            break
                    
                    if end_j != -1:
                        lines[i] = lines[i].replace('{', '{\n        try\n        {', 1)
                        idx = lines[end_j].rfind('}')
                        lines[end_j] = lines[end_j][:idx] + '} catch { }\n    }' + lines[end_j][idx+1:]
                
                out_lines.append(lines[i])
        else:
            out_lines.append(line)
        i += 1
    return '\n'.join(out_lines)

def fix_dispatcher(content):
    return re.sub(r'(?<!\?)DispatcherQueue\.TryEnqueue', r'DispatcherQueue?.TryEnqueue', content)

def fix_xamlroot(content):
    lines = content.split('\n')
    for i in range(len(lines)):
        if '.ShowAsync()' in lines[i] and 'await' in lines[i]:
            has_check = False
            for j in range(max(0, i-5), i):
                if 'XamlRoot == null' in lines[j] or 'this.XamlRoot == null' in lines[j] or 'dlg.XamlRoot == null' in lines[j]:
                    has_check = True
                    break
            if not has_check:
                indent = re.match(r'^\s*', lines[i]).group()
                m = re.search(r'await\s+([a-zA-Z0-9_]+)\.ShowAsync', lines[i])
                if m:
                    dlg_var = m.group(1)
                    lines[i] = f"{indent}if ({dlg_var}.XamlRoot == null && this.XamlRoot == null) return;\n{indent}if ({dlg_var}.XamlRoot == null) {dlg_var}.XamlRoot = this.XamlRoot;\n" + lines[i]
                else:
                    lines[i] = f"{indent}if (this.XamlRoot == null) return;\n" + lines[i]
    return '\n'.join(lines)

def fix_process_start(content):
    lines = content.split('\n')
    for i in range(len(lines)):
        # only match exact Process.Start
        if 'Process.Start(' in lines[i] and not 'using' in lines[i]:
            # we might have already matched it, but replacing just the Process.Start part
            m = re.search(r'([a-zA-Z0-9_\.]*Process\.Start\([^)]*\))', lines[i])
            if m:
                stmt = m.group(1)
                lines[i] = lines[i].replace(stmt, f"using ({stmt}) {{ }}")
    return '\n'.join(lines)

for file in files:
    try:
        with open(file, 'r', encoding='utf-8') as f:
            content = f.read()
    except UnicodeDecodeError:
        with open(file, 'r', encoding='latin1') as f:
            content = f.read()
    
    content = fix_async_void(content)
    content = fix_dispatcher(content)
    content = fix_xamlroot(content)
    content = fix_process_start(content)
    
    with open(file, 'w', encoding='utf-8') as f:
        f.write(content)

print("Done")
