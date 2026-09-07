import sys

file_path = 'src/BanglaHost.Core/Mailpit.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# Mailpit
old_mailpit = '''        JobManager.Add(p);

        return true;
    }'''

new_mailpit = '''        JobManager.Add(p);

        for (var i = 0; i < 15 && !Running(); i++) System.Threading.Thread.Sleep(300);
        return Running();
    }'''
content = content.replace(old_mailpit, new_mailpit)

# Mailhog
old_mailhog = '''        JobManager.Add(p);

        return true;
    }'''

new_mailhog = '''        JobManager.Add(p);

        for (var i = 0; i < 15 && !Running(); i++) System.Threading.Thread.Sleep(300);
        return Running();
    }'''
content = content.replace(old_mailhog, new_mailhog)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print('Mailpit patched successfully')
