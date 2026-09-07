import os
import sys

file_path = 'src/BanglaHost.App/Services/EngineHost.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

func_task_code = '''
    public Task<string?> Run(Func<Task> action) => Task.Run(async () =>
    {
        try { await action(); return (string?)null; }
        catch (Exception ex) { var m = Describe(ex); Append($"  [FAIL] {m}"); return m; }
    });

    public Task<(bool ok, string output)> RunCaptured(Func<Task> action) => Task.Run(async () =>
    {
        var sb = new System.Text.StringBuilder();
        void Cap(string l) => sb.AppendLine(l);
        LogAppended += Cap;
        var ok = true;
        try { await action(); }
        catch (Exception ex) { Append($"  [FAIL] {Describe(ex)}"); ok = false; }
        finally { LogAppended -= Cap; }
        return (ok, sb.ToString().Trim());
    });

    public async Task RunTracked(string name, Func<Task> action)
    {
        if (CurrentOp is { Running: true }) { await Run(action); return; }

        var op = new OpState { Name = name, Running = true, Progress = -1, Message = "Starting..." };
        CurrentOp = op;
        void Cap(string l) { var t = l.Trim(); if (t.Length > 0) { op.Message = t; RaiseOp(); } }
        LogAppended += Cap;
        Downloader.OnProgress = p => { op.Progress = p; RaiseOp(); };
        RaiseOp();
        try
        {
            await Task.Run(action);
            op.Success = true; op.Message = $"[OK] {name} done";
        }
        catch (Exception ex)
        {
            var m = Describe(ex); Append($"  [FAIL] {m}");
            op.Success = false; op.Message = "[FAIL] " + m;
        }
        finally
        {
            op.Running = false; op.Progress = 100;
            Downloader.OnProgress = null;
            LogAppended -= Cap;
            RaiseOp();
        }
    }
'''

content = content.replace('public Task<string?> Run(Action action)', func_task_code + '\n    public Task<string?> Run(Action action)')

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print('Patched successfully')
