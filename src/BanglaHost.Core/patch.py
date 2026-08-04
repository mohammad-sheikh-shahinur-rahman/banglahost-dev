import sys, os

def replace_in_file(filename, search, replace):
    filepath = os.path.join(r'I:\BanglaHost\src\BanglaHost.Core', filename)
    try:
        content = open(filepath, 'r', encoding='windows-1252').read()
    except Exception as e:
        print(f'Error reading {filename}: {e}')
        return
    if search not in content:
        # try removing carriage returns in search
        search_no_cr = search.replace('\n', '\r\n')
        if search_no_cr in content:
            search = search_no_cr
        else:
            print(f'Search string not found in {filename}')
            return
    
    content = content.replace(search, replace.replace('\n', '\r\n'))
    open(filepath, 'w', encoding='windows-1252').write(content)
    print(f'Updated {filename}')

replace_in_file('Apache.cs', 
    'Process.Start(psi);\n        for (var i = 0; i < 12 && !Running(); i++)', 
    'var p = Process.Start(psi)!;\n        JobManager.Add(p);\n        for (var i = 0; i < 12 && !Running(); i++)')

replace_in_file('DbServer.cs', 
    'var proc = Process.Start(psi);\n        if (proc is null) return (false, "failed to spawn mysqld");\n        Directory.CreateDirectory(Paths.Run);', 
    'var proc = Process.Start(psi);\n        if (proc is null) return (false, "failed to spawn mysqld");\n        JobManager.Add(proc);\n        Directory.CreateDirectory(Paths.Run);')

replace_in_file('Mailpit.cs', 
    'var p = Process.Start(psi);\n        if (p is null) return false;\n        Directory.CreateDirectory(Paths.Run);', 
    'var p = Process.Start(psi);\n        if (p is null) return false;\n        JobManager.Add(p);\n        Directory.CreateDirectory(Paths.Run);')

replace_in_file('Nginx.cs', 
    'var proc = Process.Start(psi)!;\n        if (!wait)', 
    'var proc = Process.Start(psi)!;\n        JobManager.Add(proc);\n        if (!wait)')

replace_in_file('NodeSite.cs', 
    'try { proc = Process.Start(psi); }\n        catch (Exception) { return false; }\n        if (proc is null) return false;\n        Directory.CreateDirectory(Paths.Run);', 
    'try { proc = Process.Start(psi); }\n        catch (Exception) { return false; }\n        if (proc is null) return false;\n        JobManager.Add(proc);\n        Directory.CreateDirectory(Paths.Run);')

replace_in_file('PhpCgi.cs', 
    'var proc = Process.Start(psi);\n        if (proc is null) return false;\n        Directory.CreateDirectory(Paths.Run);', 
    'var proc = Process.Start(psi);\n        if (proc is null) return false;\n        JobManager.Add(proc);\n        Directory.CreateDirectory(Paths.Run);')

replace_in_file('PySite.cs', 
    'try { proc = Process.Start(psi); }\n        catch (Exception ex) { return (false, $"failed to start the app: {ex.Message}"); }\n        if (proc is null) return (false, "failed to spawn the process");\n        Directory.CreateDirectory(Paths.Run);', 
    'try { proc = Process.Start(psi); }\n        catch (Exception ex) { return (false, $"failed to start the app: {ex.Message}"); }\n        if (proc is null) return (false, "failed to spawn the process");\n        JobManager.Add(proc);\n        Directory.CreateDirectory(Paths.Run);')

replace_in_file('SearchServers.cs', 
    'var p = Process.Start(psi);\n        if (p is null) return false;\n        Directory.CreateDirectory(Paths.Run);', 
    'var p = Process.Start(psi);\n        if (p is null) return false;\n        JobManager.Add(p);\n        Directory.CreateDirectory(Paths.Run);')
