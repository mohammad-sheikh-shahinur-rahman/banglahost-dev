using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BanglaHost.Core;

public class ProfileFunction
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public long Cost { get; set; }
}

public static class ProfilerParser
{
    public static List<ProfileFunction> ParseCachegrind(string path)
    {
        var funcs = new Dictionary<string, ProfileFunction>();
        string currentFn = "";
        string currentFl = "";
        
        var fnMap = new Dictionary<string, string>();
        var flMap = new Dictionary<string, string>();

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("fl="))
            {
                var val = line[3..].Trim();
                if (val.StartsWith("("))
                {
                    var end = val.IndexOf(')');
                    var id = val[..end];
                    if (val.Length > end + 1)
                    {
                        var name = val[(end + 1)..].Trim();
                        flMap[id] = name;
                        currentFl = name;
                    }
                    else currentFl = flMap.TryGetValue(id, out var m) ? m : val;
                }
                else currentFl = val;
            }
            else if (line.StartsWith("fn="))
            {
                var val = line[3..].Trim();
                if (val.StartsWith("("))
                {
                    var end = val.IndexOf(')');
                    var id = val[..end];
                    if (val.Length > end + 1)
                    {
                        var name = val[(end + 1)..].Trim();
                        fnMap[id] = name;
                        currentFn = name;
                    }
                    else currentFn = fnMap.TryGetValue(id, out var m) ? m : val;
                }
                else currentFn = val;
            }
            else if (char.IsDigit(line.FirstOrDefault()))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out var cost))
                {
                    var key = $"{currentFl}::{currentFn}";
                    if (!funcs.TryGetValue(key, out var pf))
                    {
                        pf = new ProfileFunction { Name = currentFn, File = currentFl, Cost = 0 };
                        funcs[key] = pf;
                    }
                    pf.Cost += cost;
                }
            }
        }
        
        return funcs.Values.OrderByDescending(x => x.Cost).ToList();
    }
}
