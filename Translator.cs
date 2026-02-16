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
        public static bool IsLoggingMissing = false; // 缺失文本记录开关
        // public static float FontSizeScale = 1.0f; // 字体缩放比例 (1.0 = 100%) (暂时注释)
        
        private static Dictionary<string, string> _translations = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _reverseTranslations = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Dictionary<string, string>> _scopedDictionaries = new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _forceScopedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _missingTranslations = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _runtimeCache = new Dictionary<string, string>();
        private static string _translationFilePath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "translation.json");
        private static string _missingFilePath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "missing.json");
        private static string _scopesDirPath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "scopes");
        private static string _forceScopesFilePath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "force_scopes.json");

        public static void ClearCache()
        {
            _runtimeCache.Clear();
            _scopedDictionaries.Clear();
        }

        // --- 核心过滤器正则表达式 (重构版) ---
        // 1. (可选的正负号/符号) + 纯数字 + (可选的单位)
        private static readonly Regex ValueUnitNoiseRegex = new Regex(@"^[+-]?\s*\$?\d*[\d.]+\s*[a-zA-Z/°%]{0,6}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 2. 游戏特有的标签数据 (如 x 2, CAPACITOR 8 kJ, V -19.8, $10.0k, 380.1M$, Mag x7.2, H 155.5, M 1.39)
        private static readonly Regex SpecialTagNoiseRegex = new Regex(@"^(x\s*\d+|capacitor\s*[\d.]+\s*[a-z]*|[vhm]\s*[+-]?\s*[\d.]+|[\$¥€]\s*[\d.]+[kKmM]?|[\d.]+[kKmM]\s*[\$¥€]|mag\s*x\s*\d*[\d.]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 3. 特殊排除项/坐标 (如 Hi67, Ji97, Ia, Ff, Jj)
        private static readonly Regex CoordinateNoiseRegex = new Regex(@"^[A-Z][a-z](\d{1,4})?$", RegexOptions.Compiled);
        // 4. 分隔符正则 (优先级：特殊专有名词 > Markdown 块 > 双减号 > 单标签 > 符号)
        private static readonly Regex DelimiterRegex = new Regex(@"(T/A-30|<[^>]+>.*?</[^>]+>|<[^>]+>|--|\s+-\s+|[:/\[\]()|\n\v])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 5. 纯符号/数字备份判定
        private static readonly Regex PureSymbolsRegex = new Regex(@"^[+\-0-9\s./()\[\]%#@&*|<>—]+$", RegexOptions.Compiled);
        // 6. 版本号号过滤正则 (匹配: 文本 + version + 可选空格 + 数字.数字.数字)
        private static readonly Regex VersionFilterRegex = new Regex(@"^(.*?version.*?)\s*(\d+\.\d+\.\d+.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 7. 单词+数字过滤正则 (匹配: 纯单词文本 + 空格 + 可选正负号 + 数字/小数)
        private static readonly Regex WordNumberRegex = new Regex(@"^([a-zA-Z\s]+)\s+([+-]?\d+(?:\.\d+)?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 8. 爆炸物载荷过滤正则 (匹配: 数字 + kg/kt + TNT)
        private static readonly Regex ExplosiveNoiseRegex = new Regex(@"^\d*[\d.]+\s*(kg|kt)\s*tnt$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 9. 末尾数量过滤 (匹配: 文本 + 空格 + x + 数字)
        private static readonly Regex EndQuantityRegex = new Regex(@"^(.*?)\s+x\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 10. 跑道动态信息过滤 (升级版:匹配 文本 + Runway + 数字 + 随后的距离等内容)
        private static readonly Regex EndRunwayRegex = new Regex(@"^(.*?\brunway)\s+\d{1,2}.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 11. 单词 + 数值单位过滤 (匹配: 文本 + 空格 + 可选正负号 + 数字 + 单位)
        private static readonly Regex EndValueUnitRegex = new Regex(@"^(.*?)\s+[+-]?\d*[\d.]+\s*([a-zA-Z°%]{1,3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 12. 距离+方向指示符过滤 (如 "434m<", "22km<")
        private static readonly Regex DistanceIndicatorNoiseRegex = new Regex(@"^\d*[\d.]+\s*(m|km)\s*<$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 13. 纯数值 + 单位模式 (如 "500 rounds", "10 knots")
        private static readonly Regex NumberUnitOnlyRegex = new Regex(@"^(\d*[\d.]+)\s*([a-zA-Z/°%]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 14. Score + 小数过滤 (匹配 Score + 任意位小数，提取 Score 部分)
        private static readonly Regex ScoreNumberRegex = new Regex(@"^(score\s*)[\d.]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 15. 复合句式过滤：Booting [内容] [点]
        private static readonly Regex BootingSentenceRegex = new Regex(@"^(Booting)\s+(.+?)(\.{0,4})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 16. 复合句式过滤：Buy [内容]
        private static readonly Regex BuySentenceRegex = new Regex(@"^(Buy)\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 17. 复合句式过滤：[1-3词] set to [1-2词]
        private static readonly Regex SetToSentenceRegex = new Regex(@"^((?:\S+\s+){0,2}\S+)\s+(set to)\s+((?:\S+\s+){0,1}\S+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 18. 单词 + 加号数字 (最多4词 + 数字)
        private static readonly Regex EndPlusNumberRegex = new Regex(@"^(.+?)\s*\+\s*(\d+(?:\.\d{1,5})?)$", RegexOptions.Compiled);
        // 20. Runway/Taxi 复合句式
        private static readonly Regex TaxiToSentenceRegex = new Regex(@"^(Cleared to taxi to|Taxi to)\s+(runway\s+\d{1,2}|(?:\S+\s+){1,2}\S+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 21. Turret Control 复合句式
        private static readonly Regex TurretControlRegex = new Regex(@"^(.*?)\s+(Turret|Turrent)\s+(under\s+pilot\s+control)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static void LoadTranslations()
        {
            _runtimeCache.Clear();
            _scopedDictionaries.Clear();
            _forceScopedNames.Clear();
            Plugin.Logger.LogInfo("Clearing runtime cache and loading translations...");
            
            // 0. 加载强制作用域白名单
            if (File.Exists(_forceScopesFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_forceScopesFilePath);
                    var list = JsonConvert.DeserializeObject<List<string>>(json);
                    if (list != null)
                    {
                        foreach (var name in list) _forceScopedNames.Add(name);
                        Plugin.Logger.LogInfo($"Loaded {_forceScopedNames.Count} force-scoped object names.");
                    }
                }
                catch (System.Exception ex) { Plugin.Logger.LogError($"Error loading force_scopes.json: {ex.Message}"); }
            }
            else
            {
                // 创建示例文件
                File.WriteAllText(_forceScopesFilePath, JsonConvert.SerializeObject(new List<string> { "RadarDisplay", "FuelGauge" }, Formatting.Indented));
            }

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
                            // 修复：反向字典应存储剥离作用域后的原文，防止还原时出现物品名
                            string reverseValue = cleanedKey;
                            if (reverseValue.StartsWith("[") && reverseValue.Contains("]"))
                            {
                                int closeIndex = reverseValue.IndexOf(']');
                                if (closeIndex > 0 && closeIndex < reverseValue.Length - 1)
                                {
                                    reverseValue = reverseValue.Substring(closeIndex + 1);
                                }
                            }
                            _reverseTranslations[kvp.Value] = reverseValue;
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
                        // 提取纯文本部分作为 Value，方便直接翻译
                        string displayValue = key;
                        if (key.StartsWith("[") && key.Contains("]"))
                        {
                            int closeBracketIndex = key.IndexOf(']');
                            if (closeBracketIndex > 0 && closeBracketIndex < key.Length - 1)
                            {
                                displayValue = key.Substring(closeBracketIndex + 1);
                            }
                        }
                        
                        missingDict[key] = displayValue;
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
            
            // 0. 特解：如果翻译已禁用，尝试还原原文 (仅针对已翻译成中文的情况)
            if (!IsEnabled)
            {
                string tagStripped = Regex.Replace(text, @"<[^>]+>", "");
                string cleaned = tagStripped.Trim();
                // 只有当文本包含中文时，才尝试进行反向还原
                if (HasChinese(cleaned) && _reverseTranslations.TryGetValue(cleaned, out string original))
                {
                    return text.Replace(tagStripped, original);
                }
                return text;
            }

            // 如果有作用域，多级查询优先于全局
            bool isForceScoped = !string.IsNullOrEmpty(scope) && _forceScopedNames.Contains(scope);

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

                // 如果属于强制作用域，且在这里没找到翻译，则直接判定为不翻译（不进入全局查找）
                if (isForceScoped) return text;
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

            // --- B1. 特殊预处理：Turret Control 模式 ---
            var turretMatch = TurretControlRegex.Match(text.Trim());
            if (turretMatch.Success)
            {
                string head = turretMatch.Groups[1].Value.Trim();
                string turret = turretMatch.Groups[2].Value;
                string control = turretMatch.Groups[3].Value;
                // head 为前半部分，递归翻译；turret 和 control 作为独立 token 翻译
                return (string.IsNullOrEmpty(head) ? "" : TranslatePart(head, scope) + " ") 
                    + TranslateToken(turret, scope) + " " + TranslateToken(control, scope);
            }

            bool isForceScoped = !string.IsNullOrEmpty(scope) && _forceScopedNames.Contains(scope);

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

                // 如果强制作用域在此处未中，则不继续 A 阶段的全局匹配
                if (isForceScoped) goto SkipGlobalA;
            }

            if (_translations.TryGetValue(cleanedKey, out string wholeTrans))
            {
                if (stripped.Length > 0) return text.Replace(stripped, wholeTrans);
            }

        SkipGlobalA:
            // --- B. 特殊句式匹配 (Smart Sentence Splitters) ---
            // 1. Booting Pattern
            var bMatch = BootingSentenceRegex.Match(text);
            if (bMatch.Success)
            {
                string bHead = bMatch.Groups[1].Value;
                string bMid = bMatch.Groups[2].Value;
                string bTail = bMatch.Groups[3].Value;
                return TranslateToken(bHead, scope) + " " + TranslatePart(bMid, scope) + bTail;
            }
            // 2. Buy Pattern
            var buyMatch = BuySentenceRegex.Match(text);
            if (buyMatch.Success)
            {
                string buyHead = buyMatch.Groups[1].Value;
                string buyRem = buyMatch.Groups[2].Value;
                return TranslateToken(buyHead, scope) + " " + TranslatePart(buyRem, scope);
            }
            // 3. Set To Pattern
            var setMatch = SetToSentenceRegex.Match(text);
            if (setMatch.Success)
            {
                string sHead = setMatch.Groups[1].Value;
                string sMid = setMatch.Groups[2].Value; // "set to"
                string sTail = setMatch.Groups[3].Value;
                return TranslatePart(sHead, scope) + " " + TranslateToken(sMid, scope) + " " + TranslatePart(sTail, scope);
            }
            // 4. Taxi To Pattern
            var taxiMatch = TaxiToSentenceRegex.Match(text);
            if (taxiMatch.Success)
            {
                string taxiHead = taxiMatch.Groups[1].Value; // "Cleared to taxi to" or "Taxi to"
                string taxiRem = taxiMatch.Groups[2].Value;  // "runway 03" or "Short Ski-jump"
                return TranslateToken(taxiHead, scope) + " " + TranslatePart(taxiRem, scope);
            }

            // --- C. 多级切片逻辑 (优先级：Markdown块 > 单个标签 > 分隔符) ---
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

                if (isForceScoped) continue; // 强制作用域下，如果局部没中，不再尝试全局字典

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
                    string patternResult = ApplyPatterns(p, out handled, scope);
                    if (patternResult != p)
                    {
                        parts[i] = patternResult;
                        changed = true;
                    }
                    else if (!handled)
                    {
                        // d. 最后：如果不是噪音，则记录缺失
                        if (!IsPartNoise(trimmedP)) TryRecordMissing(trimmedP, scope);
                    }
                }
            }

            return changed ? string.Concat(parts) : text;
        }

        private static string ApplyPatterns(string text, out bool handled, string scope = null)
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
                    TryRecordMissing(prefix, scope);
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
                TryRecordMissing(prefix, scope);
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
                TryRecordMissing(prefix, scope);
                return text;
            }

            // 4. 末尾数量 (x2)
            var eqMatch = EndQuantityRegex.Match(text);
            if (eqMatch.Success)
            {
                handled = true;
                string prefix = eqMatch.Groups[1].Value.Trim();
                if (_translations.TryGetValue(prefix, out string trans)) return text.Replace(prefix, trans);
                TryRecordMissing(prefix, scope);
                return text;
            }

            // 5. 单词 + 数字 (Rank 1)
            var wnMatch = WordNumberRegex.Match(text);
            if (wnMatch.Success)
            {
                handled = true;
                string prefix = wnMatch.Groups[1].Value.Trim();
                if (_translations.TryGetValue(prefix, out string trans)) return text.Replace(prefix, trans);
                TryRecordMissing(prefix, scope);
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

            // 7. Score + 小数 (Score 123.456)
            var sMatch = ScoreNumberRegex.Match(text);
            if (sMatch.Success)
            {
                handled = true;
                string prefix = sMatch.Groups[1].Value; // "Score "
                string trimmedPrefix = prefix.Trim();  // "Score"
                string suffix = text.Substring(prefix.Length); // "123.456"

                string trans = TranslateToken(trimmedPrefix, scope);
                return trans + " " + suffix;
            }

            // 8. Booting + 内容 + 多个句号 (Booting FS-20 Vortex...)
            var bMatch = BootingSentenceRegex.Match(text);
            if (bMatch.Success)
            {
                handled = true;
                string bootPart = bMatch.Groups[1].Value;    // "Booting"
                string contentPart = bMatch.Groups[2].Value; // "FS-20 Vortex"
                string dotsPart = bMatch.Groups[3].Value;    // "..."

                string bootTrans = TranslateToken(bootPart, scope);
                string contentTrans = TranslatePart(contentPart, scope);

                return $"{bootTrans} {contentTrans}{dotsPart}";
            }

            // 9. Buy [Aircraft]
            var buyMatch = BuySentenceRegex.Match(text);
            if (buyMatch.Success)
            {
                handled = true;
                string buyPart = buyMatch.Groups[1].Value;    // "Buy"
                string contentPart = buyMatch.Groups[2].Value; // Aircraft name

                string buyTrans = TranslateToken(buyPart, scope);
                string contentTrans = TranslatePart(contentPart, scope);

                return $"{buyTrans} {contentTrans}";
            }

            // 10. [Setting] set to [Value]
            var setMatch = SetToSentenceRegex.Match(text);
            if (setMatch.Success)
            {
                handled = true;
                string settingPart = setMatch.Groups[1].Value; // "Vectoring Mode"
                string setToPart = setMatch.Groups[2].Value;   // "set to"
                string valuePart = setMatch.Groups[3].Value;   // "ShortTakeoff"

                string settingTrans = TranslatePart(settingPart, scope);
                string setToTrans = TranslateToken(setToPart, scope);
                string valueTrans = TranslatePart(valuePart, scope);

                return $"{settingTrans} {setToTrans} {valueTrans}";
            }

            // 11. 末尾 +数字 (Impact Speed + 15.5)
            var epnMatch = EndPlusNumberRegex.Match(text);
            if (epnMatch.Success)
            {
                string prefix = epnMatch.Groups[1].Value.Trim();
                int wordCount = prefix.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount <= 4)
                {
                    handled = true;
                    string trans = TranslatePart(prefix, scope);
                    // 重新拼合，保留原有的 + 符号位置
                    string plusPart = text.Substring(epnMatch.Groups[1].Length); 
                    return trans + plusPart;
                }
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

        private static void TryRecordMissing(string text, string scope = null)
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

            // 特殊处理 单词 + 数值单位：剥离
            var evuMatch = EndValueUnitRegex.Match(text);
            if (evuMatch.Success)
            {
                text = evuMatch.Groups[1].Value.Trim();
            }

            // 特殊处理 单词 + 加号数字：剥离
            var epnMatch = EndPlusNumberRegex.Match(text);
            if (epnMatch.Success)
            {
                string prefix = epnMatch.Groups[1].Value.Trim();
                if (prefix.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Length <= 4)
                {
                    text = prefix;
                }
            }
            
            if (HasChinese(text) || !IsPureAlpha(text)) return;

            string finalKey = string.IsNullOrEmpty(scope) ? text : $"[{scope}]{text}";
            
            if (!_missingTranslations.Contains(finalKey) && !_translations.ContainsKey(finalKey))
            {
                _missingTranslations.Add(finalKey);
            }
        }

        private static string TranslateToken(string token, string scope = null)
        {
            if (string.IsNullOrEmpty(token)) return token;
            string trimmed = token.Trim();
            
            // 优先查找作用域
            if (!string.IsNullOrEmpty(scope))
            {
                if (_scopedDictionaries.TryGetValue(scope, out var dict) && dict.TryGetValue(trimmed, out string st))
                    return st;
                if (_translations.TryGetValue($"[{scope}]{trimmed}", out string st2))
                    return st2;
            }
            
            // 查找全局
            if (_translations.TryGetValue(trimmed, out string trans))
                return trans;
            
            // 记录缺失并返回原词
            TryRecordMissing(trimmed, scope);
            return token;
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
