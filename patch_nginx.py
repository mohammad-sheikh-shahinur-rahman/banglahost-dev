import sys

file_path = 'src/BanglaHost.Core/Nginx.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

old_sleep = "System.Threading.Thread.Sleep(400);"
new_sleep = "for (var i = 0; i < 15 && !Running(); i++) System.Threading.Thread.Sleep(300);"

content = content.replace(old_sleep, new_sleep)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print('Nginx patched successfully')
