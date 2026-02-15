using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using Newtonsoft.Json;

namespace NuclearOptionChinese
{
    public static class Translator
    {
        public static bool IsEnabled = true; // 翻译总开关
        public static bool IsLoggingMissing = true; // 缺失文本记录开关
        // public static float FontSizeScale = 1.0f; // 字体缩放比例 (1.0 = 100%) (暂时注释)
        
        private static Dictionary<string, string> _translations = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _reverseTranslations = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Dictionary<string, string>> _scopedDictionaries = new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _missingTranslations = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _runtimeCache = new Dictionary<string, string>();
        private static string _translationFilePath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "translation.json");
        private static string _missingFilePath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "missing.json");
        private static string _scopesDirPath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "scopes");

        public static void ClearCache()
        {
            _runtimeCache.Clear();
            _scopedDictionaries.Clear();
        }

        // --- 核心过滤器正则表达式 (重构版) ---
        // 1. (可选的正负号/符号) + 纯数字 + (可选的单位)
        private static readonly Regex ValueUnitNoiseRegex = new Regex(@"^[+-]?\s*\$?\d*[\d.]+\s*[a-zA-Z/°%]{0,6}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 2. 游戏特有的标签数据 (如 x 2, CAPACITOR 8 kJ, V -19.8, $10.0k, 380.1M$, Mag x7.2)
        private static readonly Regex SpecialTagNoiseRegex = new Regex(@"^(x\s*\d+|capacitor\s*[\d.]+\s*[a-z]*|v\s*[+-]?\s*[\d.]+|[\$¥€]\s*[\d.]+[kKmM]?|[\d.]+[kKmM]\s*[\$¥€]|mag\s*x\s*\d*[\d.]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 3. 特殊排除项/坐标 (如 Hi67, Ji97, Ia, Ff, Jj)
        private static readonly Regex CoordinateNoiseRegex = new Regex(@"^[A-Z][a-z](\d{1,4})?$", RegexOptions.Compiled);
        // 4. 分隔符正则 (优先级：特殊专有名词 > Markdown 块 > 双减号 > 单标签 > 符号)
        private static readonly Regex DelimiterRegex = new Regex(@"(T/A-30|<[^>]+>.*?</[^>]+>|<[^>]+>|--|\s+-\s+|[:/\[\]()|\n\v])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 5. 纯符号/数字备份判定
        private static readonly Regex PureSymbolsRegex = new Regex(@"^[+\-0-9\s./()\[\]%#@&*|<>—]+$", RegexOptions.Compiled);
        // 6. 版本号号过滤正则 (匹配: 文本 + version + 可选空格 + 数字.数字.数字)
        private static readonly Regex VersionFilterRegex = new Regex(@"^(.*?version.*?)\s*(\d+\.\d+\.\d+.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 7. 单词+数字过滤正则 (匹配: 纯单词文本 + 空格 + 数字)
        private static readonly Regex WordNumberRegex = new Regex(@"^([a-zA-Z\s]+)\s+(\d+)$", RegexOptions.Compiled);
        // 8. 爆炸物载荷过滤正则 (匹配: 数字 + kg/kt + TNT)
        private static readonly Regex ExplosiveNoiseRegex = new Regex(@"^\d*[\d.]+\s*(kg|kt)\s*tnt$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 9. 末尾数量过滤 (匹配: 文本 + 空格 + x + 数字)
        private static readonly Regex EndQuantityRegex = new Regex(@"^(.*?)\s+x\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 10. 跑道动态信息过滤 (升级版: 匹配 文本 + Runway + 数字 + 随后的距离等内容)
        private static readonly Regex EndRunwayRegex = new Regex(@"^(.*?\Brunway)\s+\d{1,2}.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 11. 单词 + 数值单位过滤 (匹配: 文本 + 空格 + 数字 + 单位)
        private static readonly Regex EndValueUnitRegex = new Regex(@"^(.*?)\s+\d*[\d.]+\s*([a-zA-Z°%]{1,3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 12. 距离+方向指示符过滤 (如 "434m<", "22km<")
        private static readonly Regex DistanceIndicatorNoiseRegex = new Regex(@"^\d*[\d.]+\s*(m|km)\s*<$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 13. 纯数值 + 单位模式 (如 "500 rounds", "10 knots")
        private static readonly Regex NumberUnitOnlyRegex = new Regex(@"^(\d*[\d.]+)\s*([a-zA-Z/°%]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static void LoadTranslations()
        {
            _runtimeCache.Clear();
            _scopedDictionaries.Clear();
            Plugin.Logger.LogInfo("Clearing runtime cache and loading translations...");
            
            // 1. 加载主翻译文件 (Global)
            if (!File.Exists(_translationFilePath))
            {
                // Create a template if it doesn't exist
                var template = new Dictionary<string, string>
                {
                    { "New Game", "新游戏" },
                    { "Settings", "设置" },
                    { "Quit", "退出" }
                };
                
                string dir = Path.GetDirectoryName(_translationFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                File.WriteAllText(_translationFilePath, JsonConvert.SerializeObject(template, Formatting.Indented, new JsonSerializerSettings { CheckAdditionalContent = false }));
            }

            try
            {
                string json = File.ReadAllText(_translationFilePath);
                var rawTranslations = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                
                // 清理加载的翻译字典，确保 Key 没有不可见字符
                _translations = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                _reverseTranslations = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in rawTranslations)
                {
                    string cleanedKey = CleanString(kvp.Key).Trim();
                    if (!string.IsNullOrEmpty(cleanedKey) && !_translations.ContainsKey(cleanedKey))
                    {
                        _translations[cleanedKey] = kvp.Value;
                        if (!string.IsNullOrEmpty(kvp.Value))
                        {
                            _reverseTranslations[kvp.Value] = cleanedKey;
                        }
                    }
                }
                
                Plugin.Logger.LogInfo($"Successfully loaded {_translations.Count} global translations.");

                // 2. 加载 Scopes 目录下的分部件翻译文件 (Priority)
                if (Directory.Exists(_scopesDirPath))
                {
                    string[] scopeFiles = Directory.GetFiles(_scopesDirPath, "*.json");
                    foreach (string file in scopeFiles)
                    {
                        try
                        {
                            string scopeName = Path.GetFileNameWithoutExtension(file);
                            string scopeJson = File.ReadAllText(file);
                            var scopeDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(scopeJson);
                            
                            if (scopeDict != null)
                            {
                                var cleanedDict = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                                foreach (var kvp in scopeDict)
                                {
                                    string ck = CleanString(kvp.Key).Trim();
                                    if (!string.IsNullOrEmpty(ck)) cleanedDict[ck] = kvp.Value;
                                }
                                _scopedDictionaries[scopeName] = cleanedDict;
                                Plugin.Logger.LogInfo($"Loaded scoped translations for: {scopeName} ({cleanedDict.Count} items)");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Plugin.Logger.LogError($"Failed to load scope file {file}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Directory.CreateDirectory(_scopesDirPath);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogError($"Failed to load translations: {ex.Message}");
            }
        }

        public static void SaveMissingTranslations()
        {
            if (_missingTranslations.Count == 0) return;

            Dictionary<string, string> missingDict = new Dictionary<string, string>();
            
            // Load existing missing file if it exists to avoid overwriting found missing ones
            if (File.Exists(_missingFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_missingFilePath);
                    missingDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch { }
            }

            bool changed = false;
            foreach (var key in _missingTranslations)
            {
                if (!missingDict.ContainsKey(key) && !_translations.ContainsKey(key))
                {
                    // 再检查一遍分部件文件，防止刚加了文件但内存还没刷新
                    bool foundInScope = false;
                    foreach (var dict in _scopedDictionaries.Values)
                    {
                        if (dict.ContainsKey(key)) { foundInScope = true; break; }
                    }
                    
                    if (!foundInScope)
                    {
                        missingDict[key] = key; // Value defaults to key for easy translation
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                File.WriteAllText(_missingFilePath, JsonConvert.SerializeObject(missingDict, Formatting.Indented));
                Plugin.Logger.LogInfo($"Saved {missingDict.Count} missing translations to missing.json");
            }
        }

        public static string Translate(string text, string scope = null)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // 0. 特解：如果翻译已禁用，尝试还原原文
            if (!IsEnabled)
            {
                string tagStripped = Regex.Replace(text, @"<[^>]+>", "");
                string cleaned = tagStripped.Trim();
                if (_reverseTranslations.TryGetValue(cleaned, out string original))
                {
                    return text.Replace(tagStripped, original);
                }
                return text;
            }

            // 如果有作用域，多级查询优先于全局
            if (!string.IsNullOrEmpty(scope))
            {
                // 1. 优先检查 Scopes 文件夹下的独立文件 (文件名即 Scope 名)
                if (_scopedDictionaries.TryGetValue(scope, out var scopeDict))
                {
                    if (scopeDict.TryGetValue(text, out string sTrans)) return sTrans;
                    
                    string cleaned = CleanString(text).Trim();
                    if (!string.IsNullOrEmpty(cleaned) && scopeDict.TryGetValue(cleaned, out string scTrans)) return scTrans;
                }

                // 2. 检查主 translation.json 中的 "[Scope]Key" 格式
                string scopedKey = $"[{scope}]{text}";
                if (_translations.TryGetValue(scopedKey, out string scopedTrans)) return scopedTrans;

                string cleanedGlobal = CleanString(text).Trim();
                if (!string.IsNullOrEmpty(cleanedGlobal))
                {
                    string scopedCleanedKey = $"[{scope}]{cleanedGlobal}";
                    if (_translations.TryGetValue(scopedCleanedKey, out string scopedCleanedTrans)) return scopedCleanedTrans;
                }
            }

            if (_runtimeCache.TryGetValue(text, out string cached)) return cached;

            string originalInput = text;

            // --- 新逻辑：优先按换行符或垂直制表符切分 ---
            if (text.Contains("\n") || text.Contains("\u000B"))
            {
                string[] lines = text.Split(new char[] { '\n', '\u000B' });
                bool lineChanged = false;
                
                // 找到所有的分隔符，以便后面重建
                var matches = Regex.Matches(text, @"[\n\v]");
                
                for (int i = 0; i < lines.Length; i++)
                {
                    string oldLine = lines[i];
                    if (string.IsNullOrWhiteSpace(oldLine)) continue;

                    string newLine = TranslatePart(oldLine, scope);
                    if (oldLine != newLine)
                    {
                        lines[i] = newLine;
                        lineChanged = true;
                    }
                }
                
                if (lineChanged)
                {
                    // 重建字符串，保留原始的分隔符
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    for (int i = 0; i < lines.Length; i++)
                    {
                        sb.Append(lines[i]);
                        if (i < matches.Count)
                        {
                            sb.Append(matches[i].Value);
                        }
                    }
                    string result = sb.ToString();
                    _runtimeCache[originalInput] = result;
                    return result;
                }
                _runtimeCache[originalInput] = text;
                return text;
            }

            // 如果没有换行，直接处理整段
            string finalResult = TranslatePart(text, scope);
            _runtimeCache[originalInput] = finalResult;
            return finalResult;
        }

        /// <summary>
        /// 内部处理逻辑：处理单行或非换行文本
        /// </summary>
        private static string TranslatePart(string text, string scope = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (HasChinese(text)) return text;

            // --- A. 整体预处理 (长文本/整体字典匹配) ---
            string stripped = Regex.Replace(text, @"<[^>]+>", "");
            string trimmed = stripped.Trim();
            string cleanedKey = CleanString(trimmed).Trim();

            // 整体范围内也优先检查 Scoped Translation
            if (!string.IsNullOrEmpty(scope))
            {
                // 1. Scopes 独立文件优先
                if (_scopedDictionaries.TryGetValue(scope, out var sDict))
                {
                    if (sDict.TryGetValue(cleanedKey, out string st)) return text.Replace(stripped, st);
                }

                // 2. 主文件格式次之
                string scopedKey = $"[{scope}]{cleanedKey}";
                if (_translations.TryGetValue(scopedKey, out string scopedTrans))
                {
                    return text.Replace(stripped, scopedTrans);
                }
            }

            if (_translations.TryGetValue(cleanedKey, out string wholeTrans))
            {
                if (stripped.Length > 0) return text.Replace(stripped, wholeTrans);
            }

            // --- B. 多级切片逻辑 (优先级：Markdown块 > 单个标签 > 分隔符) ---
            string[] parts = DelimiterRegex.Split(text);
            bool changed = false;

            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p)) continue;
                
                // 1. 如果是基本分隔符（如 : / [ ] 等），跳过
                if (p.Length == 1 && ":/[]()|\n\v".Contains(p)) continue;

                // 2. 处理标签部分
                if (p.StartsWith("<") && p.EndsWith(">"))
                {
                    // 如果是标签块 (如 <b>111</b>)，尝试提取内部文本进行递归翻译
                    var tagMatch = Regex.Match(p, @"^(<([^>]+)>)(.*)(</\2>)$");
                    if (tagMatch.Success)
                    {
                        string openTag = tagMatch.Groups[1].Value;
                        string content = tagMatch.Groups[3].Value;
                        string closeTag = tagMatch.Groups[4].Value;

                        string translatedContent = TranslatePart(content, scope);
                        if (translatedContent != content)
                        {
                            parts[i] = openTag + translatedContent + closeTag;
                            changed = true;
                        }
                    }
                    continue; // 单个标签 (如 <b>) 或已处理的块，跳过
                }

                // 3. 处理纯文本部分
                string trimmedP = p.Trim();
                if (string.IsNullOrEmpty(trimmedP) || HasChinese(trimmedP)) continue;

                // a. Scoped Part Translation
                if (!string.IsNullOrEmpty(scope))
                {
                    // Scopes 独立文件
                    if (_scopedDictionaries.TryGetValue(scope, out var pDict))
                    {
                        if (pDict.TryGetValue(trimmedP, out string pt))
                        {
                            parts[i] = p.Replace(trimmedP, pt);
                            changed = true;
                            continue;
                        }
                    }

                    // 主文件 [Scope] 格式
                    string scopedKey = $"[{scope}]{trimmedP}";
                    if (_translations.TryGetValue(scopedKey, out string scopedTrans))
                    {
                        parts[i] = p.Replace(trimmedP, scopedTrans);
                        changed = true;
                        continue;
                    }
                }

                // b. 尝试字典匹配
                if (_translations.TryGetValue(trimmedP, out string translatedP))
                {
                    parts[i] = p.Replace(trimmedP, translatedP);
                    changed = true;
                }
                else
                {
                    // c. 尝试模式匹配
                    bool handled;
                    string patternResult = ApplyPatterns(p, out handled);
                    if (patternResult != p)
                    {
                        parts[i] = patternResult;
                        changed = true;
                    }
                    else if (!handled)
                    {
                        // d. 最后：如果不是噪音，则记录缺失
                        if (!IsPartNoise(trimmedP)) TryRecordMissing(trimmedP);
                    }
                }
            }

            return changed ? string.Concat(parts) : text;
        }

        private static string ApplyPatterns(string text, out bool handled)
        {
            handled = false;

            // 1. 版本号号
            if (text.IndexOf("version", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var match = VersionFilterRegex.Match(text);
                if (match.Success)
                {
                    handled = true;
                    string prefix = match.Groups[1].Value.Trim();
                    if (_translations.TryGetValue(prefix, out string trans)) return text.Replace(prefix, trans);
                    TryRecordMissing(prefix);
                    return text;
                }
            }

            // 2. 末尾跑道 (升级版)
            var erMatch = EndRunwayRegex.Match(text);
            if (erMatch.Success)
            {
                handled = true;
                string prefix = erMatch.Groups[1].Value.Trim();
                if (_translations.TryGetValue(prefix, out string trans)) return text.Replace(prefix, trans);
                TryRecordMissing(prefix);
                return text;
            }

            // 3. 单词 + 数值单位 (SPD 10km)
            var evuMatch = EndValueUnitRegex.Match(text);
            if (evuMatch.Success)
            {
                handled = true;
                string prefix = evuMatch.Groups[1].Value.Trim();
                if (IsPartNoise(prefix)) return text;
                
                if (_translations.TryGetValue(prefix, out string trans)) return text.Replace(prefix, trans);
                TryRecordMissing(prefix);
                return text;
            }

            // 4. 末尾数量 (x2)
            var eqMatch = EndQuantityRegex.Match(text);
            if (eqMatch.Success)
            {
                handled = true;
                string prefix = eqMatch.Groups[1].Value.Trim();
                if (_translations.TryGetValue(prefix, out string trans)) return text.Replace(prefix, trans);
                TryRecordMissing(prefix);
                return text;
            }

            // 5. 单词 + 数字 (Rank 1)
            var wnMatch = WordNumberRegex.Match(text);
            if (wnMatch.Success)
            {
                handled = true;
                string prefix = wnMatch.Groups[1].Value.Trim();
                if (_translations.TryGetValue(prefix, out string trans)) return text.Replace(prefix, trans);
                TryRecordMissing(prefix);
                return text;
            }

            // 6. 纯数值 + 单位 (500 rounds)
            var nuMatch = NumberUnitOnlyRegex.Match(text);
            if (nuMatch.Success)
            {
                string value = nuMatch.Groups[1].Value;
                string unit = nuMatch.Groups[2].Value;
                
                if (_translations.TryGetValue(unit, out string transUnit))
                {
                    handled = true;
                    return text.Replace(unit, transUnit);
                }
                // 这里不设置 handled = true，让它流向最后的 IsPartNoise 判定
                // (如果 unit 是 "m" 这种噪音，就不会录入缺失)
            }

            return text;
        }

        /* 
        private static string WrapSize(string text)
        {
            if (FontSizeScale == 1.0f || string.IsNullOrEmpty(text) || text.Contains("<size=")) return text;
            return $"<size={FontSizeScale * 100:F0}%>{text}</size>";
        }
        */

        private static void TryRecordMissing(string text)
        {
            if (!IsLoggingMissing || string.IsNullOrEmpty(text)) return;

            // 特殊处理 Version：剥离版本号，只保留文本部分进入 Missing
            if (text.IndexOf("version", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var vMatch = VersionFilterRegex.Match(text);
                if (vMatch.Success)
                {
                    text = vMatch.Groups[1].Value.Trim();
                }
            }

            // 特殊处理 单词 + 数字：剥离数字部分，只保留文本部分进入 Missing
            var wnMatch = WordNumberRegex.Match(text);
            if (wnMatch.Success)
            {
                text = wnMatch.Groups[1].Value.Trim();
            }
            
            if (HasChinese(text) || !IsPureAlpha(text)) return;
            
            if (!_missingTranslations.Contains(text) && !_translations.ContainsKey(text))
            {
                _missingTranslations.Add(text);
            }
        }

        private static bool IsLongText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int spaces = 0;
            bool lastWasSpace = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace) spaces++;
                    lastWasSpace = true;
                }
                else lastWasSpace = false;
            }
            return spaces >= 5; // 5个空格即判断为 6个词
        }

        private static bool IsPartNoise(string p)
        {
            if (p.Length <= 1) return true;
            if (ValueUnitNoiseRegex.IsMatch(p)) return true;
            if (SpecialTagNoiseRegex.IsMatch(p)) return true;
            if (CoordinateNoiseRegex.IsMatch(p)) return true;
            if (PureSymbolsRegex.IsMatch(p)) return true;
            if (ExplosiveNoiseRegex.IsMatch(p)) return true;
            if (DistanceIndicatorNoiseRegex.IsMatch(p)) return true;
            return false;
        }

        /// <summary>
        /// 清理字符串中的不可见字符和控制字符
        /// </summary>
        private static string CleanString(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            bool hasSpecial = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\u200B' || c == '\uFEFF' || c == '\u200E' || c == '\u200F' || c == '\u000B' || c == '\r' || c == '\t')
                {
                    hasSpecial = true;
                    break;
                }
            }
            if (!hasSpecial) return text;

            return text.Replace("\u200B", "")
                       .Replace("\uFEFF", "")
                       .Replace("\u200E", "")
                       .Replace("\u200F", "")
                       .Replace("\u000B", " ")
                       .Replace("\r", "")
                       .Replace("\t", " ");
        }

        public static bool HasChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if (c >= '\u4E00' && c <= '\u9FFF') return true;
                if (c >= '\u3000' && c <= '\u303F') return true;
            }
            return false;
        }

        private static bool IsPureAlpha(string text)
        {
            foreach (char c in text)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return true;
            }
            return false;
        }

        private static bool HasAnyDigit(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i])) return true;
            }
            return false;
        }
    }
}
