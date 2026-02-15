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
        // 标准 Unity UI Text
        [HarmonyPatch(typeof(Text), "text", MethodType.Setter)]
        [HarmonyPrefix]
        public static void TextSetterPrefix(ref string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            value = Translator.Translate(value);
        }

        // TextMeshPro 核心文本设置 (属性)
        [HarmonyPatch(typeof(TMP_Text), "text", MethodType.Setter)]
        [HarmonyPrefix]
        public static void TMPTextSetterPrefix(TMP_Text __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            string translated = Translator.Translate(value);
            if (value != translated)
            {
                value = translated;
                // 不再手动设置字体，依赖 FontLoader 中已经注册的全局 Fallback
            }
        }

        // TextMeshPro 核心 SetText 重载
        [HarmonyPatch(typeof(TMP_Text), "SetText", typeof(string), typeof(bool))]
        [HarmonyPrefix]
        public static void TMPSetTextPrefix(TMP_Text __instance, ref string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText)) return;
            string translated = Translator.Translate(sourceText);
            if (sourceText != translated)
            {
                sourceText = translated;
            }
        }

        // 捕捉在重绘前的最后机会
        [HarmonyPatch(typeof(TMP_Text), "OnPreRenderCanvas")]
        [HarmonyPrefix]
        public static void OnPreRenderPrefix(TMP_Text __instance)
        {
            if (__instance == null) return;
            string original = __instance.text;
            if (string.IsNullOrEmpty(original)) return;

            string translated = Translator.Translate(original);
            if (original != translated)
            {
                __instance.text = translated; 
            }
        }

        // TextMeshPro SetText(string, float, ...) 等各种数值重载
        [HarmonyPatch(typeof(TMP_Text), "SetText", typeof(string), typeof(float))]
        [HarmonyPatch(typeof(TMP_Text), "SetText", typeof(string), typeof(float), typeof(float))]
        [HarmonyPatch(typeof(TMP_Text), "SetText", typeof(string), typeof(float), typeof(float), typeof(float))]
        [HarmonyPatch(typeof(TMP_Text), "SetText", typeof(string), typeof(double))]
        [HarmonyPrefix]
        public static void TMPSetTextArgsPrefix(TMP_Text __instance, ref string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText)) return;
            string translated = Translator.Translate(sourceText);
            if (sourceText != translated)
            {
                sourceText = translated;
            }
        }

        [HarmonyPatch(typeof(TMP_Text), "SetText", typeof(StringBuilder))]
        [HarmonyPrefix]
        public static void TMPSetTextSBPrefix(TMP_Text __instance, StringBuilder sourceText)
        {
            if (sourceText == null) return;
            string original = sourceText.ToString();
            string translated = Translator.Translate(original);
            if (original != translated)
            {
                sourceText.Clear();
                sourceText.Append(translated);
            }
        }

        // ================= 根源修正：OnEnable 与字体 =================

        [HarmonyPatch(typeof(TMP_Text), "OnEnable")]
        [HarmonyPrefix]
        public static void OnEnablePrefix(TMP_Text __instance)
        {
            if (__instance == null) return;
            string original = __instance.text;
            if (string.IsNullOrEmpty(original)) return;

            string translated = Translator.Translate(original);
            if (original != translated)
            {
                __instance.text = translated; 
            }
        }

        [HarmonyPatch(typeof(TextMesh), "text", MethodType.Setter)]
        [HarmonyPrefix]
        public static void TextMeshSetterPrefix(TextMesh __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            value = Translator.Translate(value);
        }
    }
}
