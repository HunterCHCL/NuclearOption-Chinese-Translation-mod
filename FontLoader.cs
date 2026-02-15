using System.Collections.Generic;
using System.IO;
using BepInEx;
using TMPro;
using UnityEngine;

namespace NuclearOptionChinese
{
    public static class FontLoader
    {
        public static TMP_FontAsset ChineseFont { get; private set; }
        private static string _fontPath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "font.ttf");
        private static string _bundlePath = Path.Combine(Paths.PluginPath, "NuclearOptionChinese", "chinesefont.assets");

        public static void LoadFont()
        {
            // Method 1: Load from AssetBundle (High quality, recommended)
            if (File.Exists(_bundlePath))
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(_bundlePath);
                if (bundle != null)
                {
                    ChineseFont = bundle.LoadAsset<TMP_FontAsset>("ChineseFont");
                    if (ChineseFont != null)
                    {
                        Plugin.Logger.LogInfo("Loaded TMP_FontAsset from bundle.");
                        TMP_Settings.fallbackFontAssets.Add(ChineseFont);
                        return;
                    }
                }
            }

            // Method 2: Load from .ttf file (Easy to use)
            if (File.Exists(_fontPath))
            {
                try
                {
                    Font unityFont = new Font(_fontPath);
                    ChineseFont = TMP_FontAsset.CreateFontAsset(unityFont);
                    ChineseFont.name = "ChineseFont_Dynamic";
                    
                    Plugin.Logger.LogInfo("Created dynamic TMP_FontAsset from .ttf file.");
                    TMP_Settings.fallbackFontAssets.Add(ChineseFont);
                }
                catch (System.Exception ex)
                {
                    Plugin.Logger.LogError($"Failed to create font from .ttf: {ex.Message}");
                }
            }
            else
            {
                Plugin.Logger.LogWarning("No font file found (font.ttf or chinesefont.assets). Chinese characters may not display.");
            }
        }
    }
}
