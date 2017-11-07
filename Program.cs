/*
Copyright 2017 Roar Flolo

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

*/
using System;
using System.Collections.Generic;
using System.Text;

namespace MemReportParser
{
    class Entry
    {
        public string Name { get; private set; }
        public long Value { get; private set; }

        public Entry(string name, long value)
        {
            Name = name;
            Value = value;
        }
    }

    class EntryHistory
    {
        public List<Entry> Entries { get; private set; }
        public List<long> Diffs { get; private set; }
        public List<long> BaseLine { get; private set; }
        public long Sum { get; private set; }
        public long Min { get; private set; }
        public long Max { get; private set; }
        public long Trend
        {
            get
            {
                if( Entries.Count>=2)
                {
                    return (Entries[Entries.Count-1].Value - Entries[0].Value) / Entries.Count;
                }
                return 0;
            }
        }

        public EntryHistory()
        {
            Entries = new List<Entry>();
            Diffs = new List<long>();
            BaseLine = new List<long>();
            Sum = 0;
            Min = long.MaxValue;
            Max = long.MinValue;
        }

        public void Add(Entry e)
        {
            long diff = 0;
            if (Entries.Count == 0)
            {
                diff = e.Value; ;
            }
            else
            {
                diff = e.Value - Entries[Entries.Count - 1].Value;
            }
            Entries.Add(e);
            Diffs.Add(diff);
            BaseLine.Add(e.Value - Entries[0].Value);
            Sum += diff;
            Min = Math.Min(Min, e.Value);
            Max = Math.Max(Max, e.Value);
        }
    }

    class ValueEntry
    {
        public string Name { get; private set; }
        public int Count {  get { return m_Entries.Count; } }
        List<Entry> m_Entries;

        public ValueEntry(string name)
        {
            Name = name;
            m_Entries = new List<Entry>();
        }

        public void Add(string name, long value)
        {
            m_Entries.Add(new Entry(name, value));
        }

        public EntryHistory Diff()
        {
            EntryHistory result = new EntryHistory();
            if (m_Entries.Count > 1)
            {
                for (int i = 1; i < m_Entries.Count; ++i)
                {
                    result.Add(m_Entries[i]);
                }
            }
            return result;
        }
    }

    class Program
    {
        enum ParseState
        {
            SEARCHING,
            MEMORY_STATS,
            OBJECT_CLASS_LIST,
            OBJECT_LIST,
            RHI_STATS,
            PERSISTENT_LEVEL,
            BINNED_ALLOCATOR_STATS,
            POOL_STATS,
            // Pooled Render Targets:
            POOLED_RENDER_TARGETS,
            TEXTURE_LIST
        }

        static string[] SplitAndTrim(string line, char splitChar)
        {
            string[] words = line.Split(splitChar);
            List<string> trimmedWords = new List<string>();
            foreach (string word in words)
            {
                if (string.IsNullOrEmpty(word) || string.IsNullOrWhiteSpace(word))
                {
                }
                else
                {
                    trimmedWords.Add(word.Trim());
                }
            }
            return trimmedWords.ToArray();
        }

        static void AddEntry(Dictionary<string, ValueEntry> dict, string name, string key, long value)
        {
            if (!dict.ContainsKey(key))
            {
                dict[key] = new ValueEntry(key);
            }
            dict[key].Add(name, value);
        }

        static Dictionary<string, ValueEntry> GetStatDict(Dictionary<string, Dictionary<string, ValueEntry>> statDict, string name)
        {
            if (!statDict.ContainsKey(name))
            {
                statDict[name] = new Dictionary<string, ValueEntry>();
            }
            return statDict[name];
        }

        static bool ParseInMemOnDisk(string line, out long inMemSize, out long onDiskSize)
        {
            //Total size: InMem = 283.43 MB OnDisk = 355.34 MB Count = 979
            int a1 = line.IndexOf("InMem= ");
            int b1 = line.IndexOf("OnDisk= ");
            if (a1 != -1 && b1 != -1)
            {
                int a2 = line.IndexOf(" MB", a1);
                int b2 = line.IndexOf(" MB", b1);
                if (a2 != -1 && b2 != -1)
                {
                    a1 += 7;
                    b1 += 8;
                    string inMemStr = line.Substring(a1, a2 - a1).Trim();
                    string onDisStr = line.Substring(b1, b2 - b1).Trim();
                    float inMemMB = 0.0f;
                    float onDiskMB = 0.0f;
                    if (float.TryParse(inMemStr, out inMemMB) && float.TryParse(onDisStr, out onDiskMB))
                    {
                        inMemSize = (long)(inMemMB * 1024.0f * 1024.0f);
                        onDiskSize = (long)(onDiskMB * 1024.0f * 1024.0f);
                        return true;
                    }
                }
            }
            inMemSize = 0;
            onDiskSize = 0;
            return false;
        }

        static void ParseBinnedMemory2(string line, string preText, string postTextA, string postTextB, string fileName, Dictionary<string, ValueEntry> dict)
        {
            string[] words = line.Replace(preText, "").Split(' ');
            float memUsed = 0.0f;
            float memWaste = 0.0f;
            if (words.Length > 0)
            {
                if (float.TryParse(words[0], out memUsed))
                {
                    string key = preText + postTextA;
                    long value = (long)(memUsed * 1024.0f);
                    AddEntry(dict, fileName, key, value);
                }
            }
            if (words.Length > 4)
            {
                if (float.TryParse(words[4], out memWaste))
                {
                    string key = preText + postTextB;
                    long value = (long)(memWaste * 1024.0f);
                    AddEntry(dict, fileName, key, value);
                }
            }
        }

        enum StatType
        {
            VALUE,
            DIFF,
            BASELINE
        }

        static void OutputStats(Dictionary<string, ValueEntry> dict, string header, StatType type)
        {
            int columnCount = 0;
            StringBuilder sb = new StringBuilder();
            StringBuilder sbh = new StringBuilder();
            foreach (var e in dict)
            {
                ValueEntry ve = e.Value;
                EntryHistory hist = ve.Diff();
                sb.Append(string.Format("{0}, {1}, {2}, {3}, {4}", e.Value.Name, hist.Min, hist.Max, hist.Sum, hist.Trend));
                switch (type)
                {
                    case StatType.BASELINE:
                        columnCount = Math.Max(columnCount, hist.BaseLine.Count);
                        foreach (var d in hist.BaseLine)
                        {
                            sb.Append(string.Format(", {0}", d));
                        }
                        break;

                    case StatType.DIFF:
                        columnCount = Math.Max(columnCount, hist.Diffs.Count);
                        foreach (var d in hist.Diffs)
                        {
                            sb.Append(string.Format(", {0}", d));
                        }
                        break;

                    case StatType.VALUE:
                        columnCount = Math.Max(columnCount, hist.Entries.Count);
                        foreach (var d in hist.Entries)
                        {
                            sb.Append(string.Format(", {0}", d.Value));
                        }
                        break;
                }
                sb.AppendLine();
            }

            sbh.AppendLine("");
            sbh.AppendLine(string.Format("{0},{1}", header, type));
            sbh.Append(string.Format("Name, Min, Max, Sum, Trend, Base[0]"));
            switch (type)
            {
                case StatType.VALUE:
                    for (int iCol = 1; iCol < columnCount; ++iCol)
                    {
                        sbh.Append(string.Format(",Val[{0}]", iCol));
                    }
                    break;

                case StatType.DIFF:
                    for (int iCol = 1; iCol < columnCount; ++iCol)
                    {
                        sbh.Append(string.Format(",{0} -> {1}", iCol - 1, iCol));
                    }
                    break;

                case StatType.BASELINE:
                    for (int iCol = 1; iCol < columnCount; ++iCol)
                    {
                        sbh.Append(string.Format(",0 -> {1}", iCol));
                    }
                    break;
            }
            sbh.AppendLine("");

            Console.Write(sbh.ToString());
            Console.WriteLine(sb.ToString());
        }


        class ArgParserLite
        {
            string[] m_Args;

            public ArgParserLite(string[] args)
            {
                m_Args = args;
            }

            public bool HasOption(string option)
            {
                bool result = false;
                for (int iArg = 0; iArg < m_Args.Length; ++iArg)
                {
                    string arg = m_Args[iArg].Trim();
                    if (arg == option)
                    {
                        result = true;
                        break;
                    }
                }
                return result;
            }

            public string GetValue(string option, string defaultValue = "")
            {
                string result = defaultValue;
                for (int iArg = 0; iArg < m_Args.Length; ++iArg)
                {
                    string arg = m_Args[iArg].Trim();
                    if (arg == option && m_Args.Length > iArg + 1)
                    {
                        result = m_Args[iArg + 1].Trim();
                        break;
                    }
                }
                return result;
            }

            public bool GetOption(string option, bool defaultValue = false)
            {
                bool result = defaultValue;
                for (int iArg = 0; iArg < m_Args.Length; ++iArg)
                {
                    string arg = m_Args[iArg].Trim();
                    if (arg == option)
                    {
                        result = true;
                        break;
                    }
                }
                return result;
            }
        }

        static void Main(string[] args)
        {
            ArgParserLite argParser = new ArgParserLite(args);
            string memReportPath = argParser.GetValue("-i", null);
            string searchPattern = argParser.GetValue("-p", null);
            string statType = argParser.GetValue("-s", "value");
            bool printHelp = argParser.GetOption("-h") || string.IsNullOrEmpty(memReportPath) || string.IsNullOrEmpty(searchPattern);
            if (printHelp)
            {
                Console.WriteLine("Parse UE4 MemReport files to see memory usage over time. For instance do a MemReport on");
                Console.WriteLine("the Main Menu, load a level, go back to the Main Menu and do another MemReport. Repeat");
                Console.WriteLine("as many times as you like. You can then spot any memory usage increase which would indicate");
                Console.WriteLine("a memory or resource leak.");
                Console.WriteLine("");
                Console.WriteLine("Usage:");
                Console.WriteLine("  -i <input path> Specify the path where we search for .memreport files.");
                Console.WriteLine("                  The folder should contain one or more .memreport files.");
                Console.WriteLine("");
                Console.WriteLine("  -p <pattern>    File pattern to search for, can contain wildcards.");
                Console.WriteLine("                  test-*.memreport");
                Console.WriteLine("");
                Console.WriteLine("  -s <stat type>  Type of statistic to output");
                Console.WriteLine("                  value - print out the actual value");
                Console.WriteLine("                  diff - print out the change in value from report to report");
                Console.WriteLine("                  baseline - print out the change in value from the first report");
                Console.WriteLine("");
                return;
            }
 
            Dictionary<string, ValueEntry> activeobjectStats = null;
            Dictionary<string, Dictionary<string, ValueEntry>> allStats = new Dictionary<string, Dictionary<string, ValueEntry>>();

            string[] files = System.IO.Directory.GetFiles(memReportPath, searchPattern);
            Array.Sort(files, (a, b) => string.Compare(a, b));
            foreach ( string file in files)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                //Console.WriteLine(string.Format("File: {0} - {1}", fileName, file));
                string[] lines = System.IO.File.ReadAllLines(file);
                ParseState state = ParseState.SEARCHING;
                foreach( string line in lines)
                {
                    //Obj List:
                    //Obj List: class=SoundWave
                    //Obj List: class=SkeletalMesh
                    //Obj List: class=StaticMesh
                    //Obj List: class=Level
                    if (line.Contains("Obj List:"))
                    {
                        string className = "";
                        if (line.Contains("class="))
                        {
                            className = line.Replace("-alphasort", "").Trim().Replace("Obj List: class=", "").Trim();
                        }
                        switch (className)
                        {
                            default:
                                state = ParseState.OBJECT_CLASS_LIST;
                                activeobjectStats = GetStatDict(allStats, "Object Classes");
                                break;
                            case "SoundWave":
                                state = ParseState.OBJECT_LIST;
                                activeobjectStats = GetStatDict(allStats, "Object Soundwaves");
                                break;
                            case "SkeletalMesh":
                                state = ParseState.OBJECT_LIST;
                                activeobjectStats = GetStatDict(allStats, "Object SkeletalMesh");
                                break;
                            case "StaticMesh":
                                state = ParseState.OBJECT_LIST;
                                activeobjectStats = GetStatDict(allStats, "Object StaticMesh");
                                break;
                            case "Level":
                                state = ParseState.OBJECT_LIST;
                                activeobjectStats = GetStatDict(allStats, "Object Level");
                                break;
                        }
                    }
                    else if (line.Contains("persistent level:"))
                    {
                        state = ParseState.PERSISTENT_LEVEL;
                    }
                    else if (string.Compare(line, "Memory Stats:", true) == 0)
                    {
                        state = ParseState.MEMORY_STATS;
                    }
                    else if (line.Contains("RHI resource memory"))
                    {
                        state = ParseState.RHI_STATS;
                    }
                    else if (line.Contains("Allocator Stats for binned:"))
                    {
                        state = ParseState.BINNED_ALLOCATOR_STATS;
                    }
                    else if (line.Contains("Block Size Num Pools"))
                    {
                        state = ParseState.POOL_STATS;
                    }
                    else if (line.Contains("Pooled Render Targets:"))
                    {
                        state = ParseState.POOLED_RENDER_TARGETS;
                    }
                    else if( line.Contains("Listing all textures."))
                    {
                        state = ParseState.TEXTURE_LIST;
                    }

                    switch (state)
                    {
                        case ParseState.SEARCHING:
                            break;

                        case ParseState.TEXTURE_LIST:
                            {
                                if( string.IsNullOrEmpty(line) )
                                {
                                    state = ParseState.SEARCHING;
                                    continue;
                                }

                                if (line.Contains("Total size: InMem"))
                                {
                                    long inMemSize = 0;
                                    long onDiskSize = 0;
                                    if(ParseInMemOnDisk(line, out inMemSize, out onDiskSize))
                                    {
                                        string key = "Total";
                                        AddEntry(GetStatDict(allStats, "TextureTotal In Mem"), fileName, key, inMemSize);
                                        AddEntry(GetStatDict(allStats, "TextureTotal On Disk"), fileName, key, onDiskSize);
                                    }
                                }
                                else if (line.Contains("Total PF_") || line.Contains("Total TEXTUREGROUP_"))
                                {
                                    //Total PF_B8G8R8A8 size: InMem = 180.20 MB OnDisk = 235.82 MB
                                    //Total PF_DXT1 size: InMem = 6.76 MB OnDisk = 7.87 MB
                                    //Total PF_DXT5 size: InMem = 63.54 MB OnDisk = 68.05 MB
                                    //Total PF_FloatRGBA size: InMem = 32.67 MB OnDisk = 43.33 MB
                                    //Total PF_BC5 size: InMem = 0.26 MB OnDisk = 0.26 MB
                                    //Total TEXTUREGROUP_World size: InMem = 71.65 MB OnDisk = 91.71 MB
                                    //Total TEXTUREGROUP_WorldNormalMap size: InMem = 0.30 MB OnDisk = 0.30 MB
                                    //Total TEXTUREGROUP_Vehicle size: InMem = 54.22 MB OnDisk = 55.56 MB
                                    //Total TEXTUREGROUP_Skybox size: InMem = 0.04 MB OnDisk = 0.04 MB
                                    //Total TEXTUREGROUP_UI size: InMem = 151.08 MB OnDisk = 200.17 MB
                                    //Total TEXTUREGROUP_Lightmap size: InMem = 0.48 MB OnDisk = 0.48 MB
                                    //Total TEXTUREGROUP_Shadowmap size: InMem = 0.33 MB OnDisk = 0.33 MB
                                    //Total TEXTUREGROUP_ColorLookupTable size: InMem = 4.28 MB OnDisk = 5.71 MB
                                    //Total TEXTUREGROUP_Bokeh size: InMem = 0.36 MB OnDisk = 0.36 MB
                                    //Total TEXTUREGROUP_Pixels2D size: InMem = 0.67 MB OnDisk = 0.67 MB
                                    long inMemSize = 0;
                                    long onDiskSize = 0;
                                    if (ParseInMemOnDisk(line, out inMemSize, out onDiskSize))
                                    {
                                        int e = line.IndexOf(" size: ");
                                        if( e!=-1)
                                        {
                                            string key = line.Substring(0, e).Replace("Total ", "").Trim();
                                            if (line.Contains("Total TEXTUREGROUP_"))
                                            {
                                                AddEntry(GetStatDict(allStats, "TextureGroup In Mem"), fileName, key, inMemSize);
                                                AddEntry(GetStatDict(allStats, "TextureGroup On Disk"), fileName, key, onDiskSize);
                                            }
                                            if (line.Contains("Total PF_"))
                                            {
                                                AddEntry(GetStatDict(allStats, "TextureFormat In Mem"), fileName, key, inMemSize);
                                                AddEntry(GetStatDict(allStats, "TextureFormat On Disk"), fileName, key, onDiskSize);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    //Cooked/OnDisk: Width x Height (Size in KB), Current/InMem: Width x Height (Size in KB), Format, LODGroup, Name, Streaming, Usage Count
                                    string[] words = SplitAndTrim(line, ',');
                                    if (words.Length == 7)
                                    {
                                        if (words[0] == "Cooked/OnDisk: Width x Height (Size in KB)")
                                        {
                                            // header
                                        }
                                        else
                                        {
                                            // 0: "256x256 (43688 KB)"
                                            // 1: "2048x2048 (32768 KB)"
                                            // 2: "PF_FloatRGBA"
                                            // 3: "TEXTUREGROUP_World"
                                            // 4: "/Engine/EngineMaterials/DefaultBloomKernel.DefaultBloomKernel"
                                            // 5: "NO"
                                            // 6: 0
                                            int inMemory = 0;
                                            if (int.TryParse(words[6], out inMemory))
                                            {
                                                string key = words[4];
                                                if (inMemory != 0)
                                                {
                                                    long inMemSizeKB = 0;
                                                    string[] inMemSize = SplitAndTrim(words[1], ' ');
                                                    if (inMemSize.Length == 3)
                                                    {
                                                        string sizeKB = inMemSize[1].Replace("(", "").Trim();
                                                        if (long.TryParse(sizeKB, out inMemSizeKB))
                                                        {
                                                            AddEntry(GetStatDict(allStats, "Texture In Mem"), fileName, key, inMemSizeKB);
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    long onDiskSizeKB = 0;
                                                    string[] inMemSize = SplitAndTrim(words[1], ' ');
                                                    if (inMemSize.Length == 3)
                                                    {
                                                        string sizeKB = inMemSize[1].Replace("(", "").Trim();
                                                        if (long.TryParse(sizeKB, out onDiskSizeKB))
                                                        {
                                                            AddEntry(GetStatDict(allStats, "Texture On Disk"), fileName, key, onDiskSizeKB);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                            
                        case ParseState.POOLED_RENDER_TARGETS:
                            {
                                if( line.Contains("render targets"))
                                {
                                    state = ParseState.SEARCHING;
                                }
                                else if (line.Length > 41)
                                {
                                    //   0.250MB  256x 256           1mip(s) HZBResultsCPU (B8G8R8A8)
                                    string sizeMB = line.Substring(0, 10).Trim().Replace("MB", "");
                                    string sizeRes = line.Substring(11, 18).Trim();
                                    string numMips = line.Substring(30, 9).Trim().Replace("mip(s)", "");
                                    string name = line.Substring(39).Trim();
                                    string key = string.Format("{0}-{1}-{2}", name, sizeRes, numMips);
                                    float valueMB = 0.0f;
                                    if( float.TryParse(sizeMB, out valueMB))
                                    {
                                        long value = (long)(valueMB * 1024.0f * 1024.0f);
                                        AddEntry(GetStatDict(allStats, "Render Target Pools"), fileName, key, value);
                                    }
                                }
                            }
                            break;

                        case ParseState.POOL_STATS:
                            {
                                string[] words = SplitAndTrim(line, ' ');
                                if( words.Length==11)
                                {
                                    {
                                        string key = string.Format("Pool {0} Cur Allocs", words[0]);
                                        long value = 0;
                                        if (long.TryParse(words[3], out value))
                                        {
                                            AddEntry(GetStatDict(allStats, "Pool Cur Alloc"), fileName, key, value);
                                        }
                                    }
                                    {
                                        string key = string.Format("Pool {0} Max Pools", words[0]);
                                        long value = 0;
                                        if (long.TryParse(words[2], out value))
                                        {
                                            AddEntry(GetStatDict(allStats, "Pool Max Alloc"), fileName, key, value);
                                        }
                                    }
                                    {
                                        string key = string.Format("Pool {0} Mem Used", words[0]);
                                        long value = 0;
                                        if (long.TryParse(words[7].Replace("K", ""), out value))
                                        {
                                            AddEntry(GetStatDict(allStats, "Pool Mem Used"), fileName, key, value);
                                        }
                                    }
                                    {
                                        string key = string.Format("Pool {0} Mem Slack", words[0]);
                                        long value = 0;
                                        if (long.TryParse(words[8].Replace("K", ""), out value))
                                        {
                                            AddEntry(GetStatDict(allStats, "Pool Mem Slack"), fileName, key, value);
                                        }
                                    }
                                }
                            }
                            break;

                        case ParseState.BINNED_ALLOCATOR_STATS:
                            {
                                Dictionary<string, ValueEntry> dict = GetStatDict(allStats, "Binned Memory");
                                if (line.Contains("Current Memory"))
                                {
                                    //Current Memory 1553.98 MB used, plus 98.58 MB waste
                                    ParseBinnedMemory2(line, "Current Memory ", "Used", "Waste", fileName, dict);
                                }
                                else if (line.Contains("Peak Memory"))
                                {
                                    //Peak Memory 1556.45 MB used, plus 99.49 MB waste
                                    ParseBinnedMemory2(line, "Peak Memory ", "Used", "Waste", fileName, dict);
                                }
                                else if (line.Contains("Current OS Memory"))
                                {
                                    //Current OS Memory 1652.56 MB, peak 1655.94 MB
                                    ParseBinnedMemory2(line, "Current OS Memory ", "Used", "Peak", fileName, dict);
                                }
                                else if (line.Contains("Current Waste"))
                                {
                                    //Current Waste 35.56 MB, peak 35.74 MB
                                    ParseBinnedMemory2(line, "Current Waste ", "Waste", "Peak", fileName, dict);
                                }
                                else if (line.Contains("Current Used"))
                                {
                                    //Current Used 1553.98 MB, peak 1556.45 MB
                                    ParseBinnedMemory2(line, "Current Used ", "Used", "Peak", fileName, dict);
                                }
                                else if (line.Contains("Current Slack"))
                                {
                                    //Current Slack 63.03 MB
                                    ParseBinnedMemory2(line, "Current Slack ", "Used", "Peak", fileName, dict);
                                }
                            }
                            break;

                        case ParseState.OBJECT_CLASS_LIST:
                            {
                                if (line.Contains("Objects (Total:"))
                                {
                                    state = ParseState.SEARCHING;
                                }
                                else
                                {
                                    // Class    Count      NumKB      MaxKB   ResExcKB  ResExcDedSysKB  ResExcShrSysKB  ResExcDedVidKB  ResExcShrVidKB     ResExcUnkKB
                                    string[] words = SplitAndTrim(line, ' ');
                                    if (words.Length == 10)
                                    {
                                        long count;
                                        if (long.TryParse(words[1], out count))
                                        {
                                            string key = words[0];
                                            long value = count;
                                            AddEntry(activeobjectStats, fileName, key, value);
                                        }
                                    }
                                }
                            }
                            break;

                        case ParseState.OBJECT_LIST:
                            {
                                if (line.Contains("Class    Count      NumKB      MaxKB"))
                                {
                                    state = ParseState.SEARCHING;
                                }
                                else
                                {
                                    // Object NumKB      MaxKB ResExcKB  ResExcDedSysKB ResExcShrSysKB  ResExcDedVidKB ResExcShrVidKB     ResExcUnkKB
                                    string[] words = SplitAndTrim(line, ' ');
                                    if (words.Length == 10)
                                    {
                                        float numKB;
                                        if (float.TryParse(words[2], out numKB))
                                        {
                                            string key = words[1];
                                            long value = (long)(numKB * 1024.0f * 1024.0f);
                                            AddEntry(activeobjectStats, fileName, key, value);
                                        }
                                    }
                                }
                            }
                            break;

                        case ParseState.RHI_STATS:
                            {
                                string[] words = SplitAndTrim(line, '-');
                                if (words.Length >= 3)
                                {
                                    string key = words[2];
                                    long value;
                                    if (long.TryParse(words[0], out value))
                                    {
                                        if (value < (long)4 * 1024 * 1024 * 1024)
                                        {
                                            AddEntry(GetStatDict(allStats, "RHI Memory"), fileName, key, value);
                                        }
                                    }
                                }
                            }
                            break;

                        case ParseState.MEMORY_STATS:
                            {
                                string[] words = SplitAndTrim(line, '-');
                                if (words.Length >= 3)
                                {
                                    string key = words[2];
                                    long value;
                                    if (long.TryParse(words[0], out value))
                                    {
                                        if (value < (long)4 * 1024 * 1024 * 1024)
                                        {
                                            AddEntry(GetStatDict(allStats, "Memory"), fileName, key, value);
                                        }
                                    }
                                }
                            }
                            break;

                        case ParseState.PERSISTENT_LEVEL:
                            {
                                string[] words = SplitAndTrim(line, ',');
                                if (words.Length == 6)
                                {
                                    string key = words[4];
                                    long value = 1;
                                    AddEntry(GetStatDict(allStats, "Persistent"), fileName, key, value);
                                }
                            }
                            break;
                    }
                }
            }

            {
                StatType type = StatType.VALUE;
                switch(statType.ToLower())
                {
                    default:
                    case "value": type = StatType.VALUE; break;
                    case "diff": type = StatType.DIFF; break;
                    case "baseline": type = StatType.BASELINE; break;
                }

                foreach (var entry in allStats)
                {
                    Dictionary<string, ValueEntry> dict = entry.Value;
                    string key = entry.Key;
                    OutputStats(dict, key, type);
                }
            }

            Console.WriteLine();
        }
    }
}
