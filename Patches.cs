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
            if (comp == null) return null;
            // 获取组件所在的 GameObject 名称，简单起见只取当前对象名。
            // 如果需要更精准，可以取 full path: GetPath(comp.transform)
            return comp.gameObject.name;
        }

        // 标准 Unity UI Text
        [HarmonyPatch(typeof(Text), "text", MethodType.Setter)]
        [HarmonyPrefix]
        public static void TextSetterPrefix(Text __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            value = Translator.Translate(value, GetScope(__instance));
        }

        [HarmonyPatch(typeof(Text), "OnEnable")]
        [HarmonyPrefix]
        public static void TextOnEnablePrefix(Text __instance)
        {
            if (__instance == null || string.IsNullOrEmpty(__instance.text)) return;
            string translated = Translator.Translate(__instance.text, GetScope(__instance));
            if (__instance.text != translated)
            {
                __instance.text = translated;
            }
        }

        // TextMeshPro 核心文本设置 (属性)
        [HarmonyPatch(typeof(TMP_Text), "text", MethodType.Setter)]
        [HarmonyPrefix]
        public static void TMPTextSetterPrefix(TMP_Text __instance, ref string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            string translated = Translator.Translate(value, GetScope(__instance));
            if (value != translated)
            {
                value = translated;
            }
        }

        // TextMeshPro 核心 SetText 重载
        [HarmonyPatch(typeof(TMP_Text), "SetText", typeof(string), typeof(bool))]
        [HarmonyPrefix]
        public static void TMPSetTextPrefix(TMP_Text __instance, ref string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText)) return;
            string translated = Translator.Translate(sourceText, GetScope(__instance));
            if (sourceText != translated)
            {
                sourceText = translated;
            }
        }

        // 捕捉在重绘前的最后机会
        [HarmonyPatch(typeof(TMP_Text), "OnPreRenderCanvas")]
        [HarmonyPatch(typeof(TMP_Text), "OnPopulateMesh", typeof(VertexHelper))]
        [HarmonyPrefix]
        public static void OnPreRenderPrefix(TMP_Text __instance)
        {
            if (__instance == null) return;
            string original = __instance.text;
            if (string.IsNullOrEmpty(original)) return;

            string translated = Translator.Translate(original, GetScope(__instance));
            if (original != translated)
            {
                __instance.text = translated; 
            }
        }

        // 处理通过字符数组设置的情况
        [HarmonyPatch(typeof(TMP_Text), "SetCharArray", typeof(char[]))]
        [HarmonyPatch(typeof(TMP_Text), "SetCharArray", typeof(char[]), typeof(int), typeof(int))]
        [HarmonyPrefix]
        public static void TMPSetCharArrayPrefix(TMP_Text __instance, char[] sourceText)
        {
            if (sourceText == null) return;
            string original = new string(sourceText);
            string translated = Translator.Translate(original, GetScope(__instance));
            if (original != translated)
            {
                // 注意：这里由于是 char[]，直接修改比较危险，此处目前仅供识别
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
            string translated = Translator.Translate(sourceText, GetScope(__instance));
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
            string translated = Translator.Translate(original, GetScope(__instance));
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

            string translated = Translator.Translate(original, GetScope(__instance));
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
            value = Translator.Translate(value, GetScope(__instance));
        }
    }
}
