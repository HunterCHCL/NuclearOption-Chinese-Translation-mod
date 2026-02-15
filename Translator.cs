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
        private static Dictionary<string, string> _translations = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _reverseTranslations = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _missingTranslations = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _runtimeCache = new Dictionary<string, string>();
        private static string _translationFilePath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "translation.json");
        private static string _missingFilePath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "missing.json");

        public static void ClearCache()
        {
            _runtimeCache.Clear();
        }

        // 改进后的正则表达式：可用于全局拆分长文本中的数值块
        // 捕获组1: 分隔符+数字; 捕获组2: 单位; 捕获组3: 闭合符号
        private static readonly Regex DataBlockRegex = new Regex(@"(\s*(?:[:(/\[]|\s[xX]|\s+|^)\s*\$?[\d.]+\s*)([a-zA-Z/%°]{0,10})(\s*[)\]]?)", RegexOptions.Compiled);
        
        // 用于拦截 Day (28) 这种 单词 + (数字) 的模式
        private static readonly Regex WordWithNumberRegex = new Regex(@"^([a-zA-Z\s]+)\s*[\(\[].*[\)\]]$", RegexOptions.Compiled);

        // 预定义常量正则，优化性能
        private static readonly Regex TimeRegex = new Regex(@"\d+:\d+", RegexOptions.Compiled);
        private static readonly Regex PureDataRegex = new Regex(@"^[+\-0-9\s./()\[\]%]+$", RegexOptions.Compiled);
        // 允许各种正负号、小数、及可选的单位 (不再允许单位中带空格，防止误伤)
        private static readonly Regex ValueUnitRegex = new Regex(@"^[+-]?\s*\$?\d*[\d.]+\s*[a-zA-Z/%°]{0,6}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 关键字后面跟随数字
        private static readonly Regex SpecialTagRegex = new Regex(@"^(rank|lvl|x|v|capacitor|r\[\d+[a-z]*\])[\s\d:+\-.a-z/%°\[\]]{0,10}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AlphaValuePrefixRegex = new Regex(@"^[a-zA-Z]:\s*[+-]?\s*\$?\d+[\d.]*[a-zA-Z]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ComplexMountRegex = new Regex(@"^\[\d+\][a-zA-Z0-9-/\s]+:\s*\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AircraftCodeRegex = new Regex(@"^[A-Z/]+-?\d+[A-Z]*$", RegexOptions.Compiled);
        private static readonly Regex CoordinateRegex = new Regex(@"^[A-Z][a-z]\d{2,3}$", RegexOptions.Compiled);
        // 新增：识别炸弹装药和半径等战斗参数 (如 "400kg TNT", "R[0.0m]")
        private static readonly Regex TntRegex = new Regex(@"^\d+[\d.]*[a-z]*\s*tnt$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RadiusRegex = new Regex(@"^r\[\s*\d+[\d.]*[a-z]*\s*\]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 新增：识别 "单位 [数值]" 或 "单位 (数值)" 这种纯数据块
        private static readonly Regex TagDataRegex = new Regex(@"^[a-zA-Z]+\s*[\[\(]\s*[\d.]+\s*[a-zA-Z/%°]*\s*[\]\)]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // 新增：识别仅有一个大写字母跟一个小写字母的模式 (如 Ab, Cd, Xi)
        private static readonly Regex TwoLetterRegex = new Regex(@"^[A-Z][a-z]$", RegexOptions.Compiled);

        public static void LoadTranslations()
        {
            _runtimeCache.Clear();
            Plugin.Logger.LogInfo("Clearing runtime cache and loading translations...");
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
                
                Plugin.Logger.LogInfo($"Successfully loaded {_translations.Count} translations.");
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
                    missingDict[key] = key; // Value defaults to key for easy translation
                    changed = true;
                }
            }

            if (changed)
            {
                File.WriteAllText(_missingFilePath, JsonConvert.SerializeObject(missingDict, Formatting.Indented));
                Plugin.Logger.LogInfo($"Saved {missingDict.Count} missing translations to missing.json");
            }
        }

        public static string Translate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // 如果翻译已禁用，尝试将当前可能的中文译文恢复为原文
            if (!IsEnabled)
            {
                string cleaned = CleanString(text).Trim();
                if (_reverseTranslations.TryGetValue(cleaned, out string original))
                {
                    return original;
                }
                return text;
            }

            if (_runtimeCache.TryGetValue(text, out string cached)) return cached;

            string originalInput = text;

            // 彻底清理不可见字符
            string cleanedText = CleanString(text);
            string trimmedText = cleanedText.Trim();

            if (string.IsNullOrEmpty(trimmedText)) return text;

            // 1. 精准屏蔽已翻译的中文
            if (HasChinese(trimmedText))
            {
                _runtimeCache[originalInput] = text;
                return text;
            }

            // 2. 屏蔽常见的“纯数据”噪音
            if (IsNoise(trimmedText))
            {
                _runtimeCache[originalInput] = text;
                return text;
            }

            // 补充：捕捉类似 "Day (28)" 的模式
            var wordWithNumMatch = WordWithNumberRegex.Match(trimmedText);
            if (wordWithNumMatch.Success)
            {
                string prefix = wordWithNumMatch.Groups[1].Value.Trim();
                string translatedPrefix = Translate(prefix);
                if (translatedPrefix != prefix)
                {
                    string result = trimmedText.Replace(prefix, translatedPrefix);
                    _runtimeCache[originalInput] = result;
                    return result;
                }
            }
            
            // 3. 【核心】直接匹配字典全句 (支持多行、空格)
            if (_translations.TryGetValue(trimmedText, out string translated))
            {
                _runtimeCache[originalInput] = translated;
                return translated;
            }

            // 4. 处理包含换行的多行文本：整句没翻译，再尝试按行翻译
            if (text.Contains("\n"))
            {
                string[] lines = text.Split('\n');
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string oldLine = lines[i];
                    string newLine = Translate(oldLine);
                    if (oldLine != newLine)
                    {
                        lines[i] = newLine;
                        changed = true;
                    }
                }
                if (changed)
                {
                    string joined = string.Join("\n", lines);
                    _runtimeCache[originalInput] = joined;
                    return joined;
                }
            }
            
            // 5. 【递归分段翻译】处理长文本中嵌入的数值单位块 (如: "960 rounds", "25mm", "x2", "R[0.5]")
            if (HasAnyDigit(trimmedText))
            {
                var match = DataBlockRegex.Match(text);
                if (match.Success)
                {
                    string prefix = text.Substring(0, match.Index);
                    string suffix = text.Substring(match.Index + match.Length);

                    // A. 翻译前半段
                    string transPrefix = prefix;
                    if (!string.IsNullOrEmpty(prefix.Trim())) transPrefix = Translate(prefix);

                    // B. 处理并翻译当前数值块的单位
                    string valPart = match.Groups[1].Value;
                    string unitPart = match.Groups[2].Value;
                    string closePart = match.Groups[3].Value;
                    
                    string transUnit = unitPart;
                    if (!string.IsNullOrEmpty(unitPart))
                    {
                        string cleanedUnit = unitPart.Trim();
                        if (_translations.TryGetValue(cleanedUnit, out string foundUnit))
                        {
                            transUnit = unitPart.Replace(cleanedUnit, foundUnit);
                        }
                        else if (IsPureAlpha(cleanedUnit) && !IsNoise(cleanedUnit))
                        {
                            if (!_missingTranslations.Contains(cleanedUnit)) _missingTranslations.Add(cleanedUnit);
                        }
                    }

                    // C. 递归翻译后半段 (支持一句话里出现多个动态数据)
                    string transSuffix = suffix;
                    if (!string.IsNullOrEmpty(suffix.Trim())) transSuffix = Translate(suffix);

                    string combined = transPrefix + (valPart + transUnit + closePart) + transSuffix;
                    _runtimeCache[originalInput] = combined;
                    return combined;
                }

                // 安全网：如果有数字但是没匹配到分段正则，说明这也是个动态文本，不应作为整体录入到 missing.json
                _runtimeCache[originalInput] = text;
                return text;
            }
            
            // 6. 记录常规全句到缺失 (排除已递归处理掉的短句)
            if (IsPureAlpha(trimmedText) && !HasChinese(trimmedText))
            {
                if (!_missingTranslations.Contains(trimmedText) && !_translations.ContainsKey(trimmedText))
                {
                    _missingTranslations.Add(trimmedText);
                }
            }
            
            _runtimeCache[originalInput] = text;
            return text;
        }

        /// <summary>
        /// 清理字符串中的不可见字符和控制字符
        /// </summary>
        private static string CleanString(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // 只有发现特殊字符时才进行昂贵的替换操作
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
                       .Replace("\u000B", " ") // 垂直制表符换成空格
                       .Replace("\r", "")      // 移除回车
                       .Replace("\t", " ");    // 制表符换成空格
        }

        public static bool HasChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                // CJK 统一汉字范围：0x4E00 - 0x9FFF
                if (c >= '\u4E00' && c <= '\u9FFF') return true;
                // 常见的中文字符/标点
                if (c >= '\u3000' && c <= '\u303F') return true;
            }
            return false;
        }

        private static bool IsNoise(string text)
        {
            if (text.Length <= 1) return true; // 单个字符通常是 X, ?, 或者特殊符号
            if (TwoLetterRegex.IsMatch(text)) return true; // 一大一小两个字母屏蔽
            
            string lower = text.ToLower();
            // 屏蔽版本号、链接、服务器列表关键词
            if (lower.Contains("version") || lower.Contains("v0.") || lower.Contains("discord.gg") || lower.Contains("http")) return true;
            if (text.Contains("|") && (text.Contains("PvE") || text.Contains("PvP"))) return true; 

            if (text.EndsWith("ms") && HasAnyDigit(text)) return true; // 延迟
            if (text.Contains(":") && TimeRegex.IsMatch(text)) return true; // 时间
            
            // 1. 纯数字/符号组合如 "[7 / 16]", "0.32.6", "+1.7", "-24"
            if (PureDataRegex.IsMatch(text)) return true; 

            // 2. 数值 + 单位 (如 250kg, $1.74m, 140km/h, +1.7m/s, 9kJ, +0.91 m/s)
            if (ValueUnitRegex.IsMatch(text)) return true;

            // 3. 带有任何富文本标签的数值 (如 "<color=#00FFFF>77km</color>", "<b>10</b>")
            if (text.StartsWith("<") && text.EndsWith(">") && text.Contains("</"))
            {
                // 剥离所有标签后检查剩余内容是否为噪音
                string stripped = Regex.Replace(text, @"<[^>]+>", "").Trim();
                if (string.IsNullOrEmpty(stripped) || IsNoise(stripped)) return true;
            }

            // 4. 游戏特有的标签数据 (如 "Rank 5", "x 0", "V -19.8", "CAPACITOR 8  kJ")
            if (SpecialTagRegex.IsMatch(lower)) return true;

            // 5. 带有单字母前缀的数值，如 "C: $10.0k", "M: 500"
            if (AlphaValuePrefixRegex.IsMatch(text)) return true;

            // 6. 复杂的挂载点数据 (如 "[2]IRM-S2: 2", "[0]GUN 27MM: 540")
            if (ComplexMountRegex.IsMatch(text)) return true;

            // 7. 常见的机型代号 (如 T/A-30, CI-22, PAB-125)
            if (AircraftCodeRegex.IsMatch(text)) return true;

            // 8. 特殊坐标/ID格式 (如 Hi67, Ji97, Ia92)
            if (CoordinateRegex.IsMatch(text)) return true;

            // 9. 战斗参数噪音 (如 400kg TNT, R[0.0m], Tank (500L))
            if (TntRegex.IsMatch(text) || RadiusRegex.IsMatch(text) || TagDataRegex.IsMatch(text)) return true;

            return false;
        }

        private static bool IsPureAlpha(string text)
        {
            // 检查是否包含至少一个英文字母，防止记录纯符号
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
