using System.Text;
using HarmonyLib;
using UnityEngine.UI;
using TMPro;
using UnityEngine;

namespace NuclearOptionChinese
{
    [HarmonyPatch]
    public static class TextPatches
    {
        private static string GetScope(Component comp)
        {
            if (comp == null) return "Unknown";
            return comp.gameObject.name;
        }

        // --- 策略：针对具体实现类进行拦截 ---

        // 1. 拦截 TextMeshProUGUI (这是游戏里最常用的 UI 文本)
        [HarmonyPatch(typeof(TextMeshProUGUI), "text", MethodType.Setter)]
        [HarmonyPrefix]
        public static void TmpUguiSetterPrefix(TextMeshProUGUI __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value) || Translator.HasChinese(value)) return;
            value = Translator.Translate(value, GetScope(__instance));
        }

        // 2. 拦截 TextMeshPro (这是 3D 世界里的文本)
        [HarmonyPatch(typeof(TextMeshPro), "text", MethodType.Setter)]
        [HarmonyPrefix]
        public static void Tmp3DSetterPrefix(TextMeshPro __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value) || Translator.HasChinese(value)) return;
            value = Translator.Translate(value, GetScope(__instance));
        }

        // 3. 拦截常用的 SetText 重载 (直接针对基类)
        [HarmonyPatch(typeof(TMP_Text), "SetText", typeof(string), typeof(bool))]
        [HarmonyPrefix]
        public static void TmpSetTextPrefix(TMP_Text __instance, ref string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText) || Translator.HasChinese(sourceText)) return;
            sourceText = Translator.Translate(sourceText, GetScope(__instance));
        }

        // 4. 标准 Unity UI 让它显示时立刻变中文
        [HarmonyPatch(typeof(Text), "OnEnable")]
        [HarmonyPostfix]
        public static void LegacyTextOnEnable(Text __instance)
        {
            if (__instance == null || string.IsNullOrEmpty(__instance.text) || Translator.HasChinese(__instance.text)) return;
            __instance.text = Translator.Translate(__instance.text, GetScope(__instance));
        }
    }
}
