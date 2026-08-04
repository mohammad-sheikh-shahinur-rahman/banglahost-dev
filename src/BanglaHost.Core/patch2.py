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
    'using (Process.Start(psi)) { }\n        for (var i = 0; i < 12 && !Running(); i++)', 
    'var p = Process.Start(psi)!;\n        JobManager.Add(p);\n        for (var i = 0; i < 12 && !Running(); i++)')
