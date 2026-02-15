using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NuclearOptionChinese
{
    [BepInPlugin("com.yourname.nuclearoption.chinese", "Nuclear Option Chinese Translation", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        private Harmony _harmony;
        private float _lastSaveTime = 0;
        private float _lastScanTime = 0;

        // GUI 相关
        private bool _showGui = false;
        private Rect _windowRect = new Rect(20, 20, 280, 220); 
        private float _scanInterval = 0.2f; // 默认 0.2  秒一次

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo("=== Nuclear Option Chinese Translation Plugin Starting ===");
            
            try 
            {
                // Load translations
                Translator.LoadTranslations();
                
                // Load font
                FontLoader.LoadFont();

                // Apply Harmony patches
                _harmony = new Harmony("com.yourname.nuclearoption.chinese");
                _harmony.PatchAll();

                // 注册场景加载事件
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => {
                    Logger.LogInfo($"Scene loaded: {scene.name}. Scanning for text...");
                    GameObject go = new GameObject("ScanTrigger");
                    go.AddComponent<ScanTrigger>();
                };
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error during plugin initialization: {ex.Message}");
            }

            Logger.LogInfo("=== Plugin Initialization Finished ===");
        }

        private class ScanTrigger : MonoBehaviour
        {
            private int frames = 0;
            void Update()
            {
                frames++;
                if (frames > 2)
                {
                    ((Plugin)FindObjectOfType<Plugin>())?.ScanAllText();
                    Destroy(gameObject);
                }
            }
        }

        private void Update()
        {
            // F11 切换 GUI
            if (Input.GetKeyDown(KeyCode.F11))
            {
                _showGui = !_showGui;
            }

            // 使用 unscaledTime 确保即使在游戏暂停时周期扫描也生效
            if (Time.unscaledTime - _lastScanTime > _scanInterval) 
            {
                if (Translator.IsEnabled) ScanAllText();
                _lastScanTime = Time.unscaledTime;
            }

            // 周期性保存缺失翻译
            if (Time.unscaledTime - _lastSaveTime > 60f)
            {
                Translator.SaveMissingTranslations();
                _lastSaveTime = Time.unscaledTime;
            }
        }

        private void OnGUI()
        {
            if (_showGui)
            {
                _windowRect = GUI.Window(999, _windowRect, DrawWindow, "中文翻译插件控制台");
            }
        }

        private void DrawWindow(int windowID)
        {
            GUILayout.BeginVertical();

            // 第一行：翻译开关
            string switchText = Translator.IsEnabled ? "关闭翻译" : "开启翻译";
            if (GUILayout.Button(switchText))
            {
                Translator.IsEnabled = !Translator.IsEnabled;
                Logger.LogInfo($"Translation is now {(Translator.IsEnabled ? "Enabled" : "Disabled")}");
                
                // 如果关闭了翻译，清空缓存并立刻扫描一次所有文本以恢复原文和原始字体
                if (!Translator.IsEnabled)
                {
                    Translator.ClearCache();
                    ScanAllText();
                }
                else
                {
                    // 重新开启时也扫描一次
                    ScanAllText();
                }
            }

            // 第二行：开关缺失文本记录
            string logText = Translator.IsLoggingMissing ? "正在记录缺失文本" : "已停止记录缺失文本";
            GUIContent logContent = new GUIContent(logText, "每扫描一次就会增量记录未翻译的文本并输出到mod所在文件夹里的missing.json里");
            if (GUILayout.Button(logContent))
            {
                Translator.IsLoggingMissing = !Translator.IsLoggingMissing;
                Logger.LogInfo($"Missing text logging is now {(Translator.IsLoggingMissing ? "Enabled" : "Disabled")}");
            }

            // 第三行：重载翻译文件
            if (GUILayout.Button("重新加载翻译文件"))
            {
                Logger.LogInfo("Reloading translations from disk...");
                Translator.LoadTranslations();
                ScanAllText();
            }

            // 第三行：刷新当前所有文本
            if (GUILayout.Button("刷新翻译文本"))
            {
                Logger.LogInfo("Manual scan triggered...");
                ScanAllText();
            }

            GUILayout.Space(10);

            // 第四行：扫描频率控制
            GUILayout.Label($"自动扫描频率: {_scanInterval:F1}s");
            _scanInterval = GUILayout.HorizontalSlider(_scanInterval, 0.2f, 1.5f);

            GUILayout.Space(10);

            // 第五行：中文字体大小控制 (暂时注释)
            /*
            GUILayout.Label($"中文字体缩放: {Translator.FontSizeScale * 100:F0}%");
            float newScale = GUILayout.HorizontalSlider(Translator.FontSizeScale, 0.5f, 1.5f);
            if (newScale != Translator.FontSizeScale)
            {
                Translator.FontSizeScale = newScale;
                Translator.ClearCache(); // 清除翻译缓存以重新应用 size 标签
                ScanAllText();
            }
            */

            GUILayout.Space(10);

            // 第六行：提示信息
            GUILayout.Label("按下F11以显示/隐藏此控制台");

            // 显示悬浮提示 (Tooltip)
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                var mousePos = Event.current.mousePosition;
                
                // 1. 临时增强背景不透明度 (避免背景文字干扰)
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f); 
                
                // 2. 绘制带换行的提示框
                GUIStyle tooltipStyle = new GUIStyle(GUI.skin.box);
                tooltipStyle.wordWrap = true;
                tooltipStyle.alignment = TextAnchor.UpperLeft;
                tooltipStyle.normal.textColor = Color.white; // 确保深色背景下文字可见
                
                float width = 200;
                float height = tooltipStyle.CalcHeight(new GUIContent(GUI.tooltip), width) + 10;
                
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, width, height), GUI.tooltip, tooltipStyle);
                
                // 3. 恢复背景颜色
                GUI.backgroundColor = oldColor;
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        public void ScanAllText()
        {
            // 通过 for 循环和 active 检查优化性能
            var allTMP = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
            for (int i = 0; i < allTMP.Length; i++)
            {
                var tmp = allTMP[i];
                if (tmp == null || !tmp.gameObject.activeInHierarchy) continue;
                
                // 强制重新运行翻译逻辑
                string current = tmp.text;
                string processed = Translator.Translate(current, tmp.gameObject.name);
                if (current != processed)
                {
                    tmp.text = processed;
                }
                
                // 强制刷新渲染状态
                tmp.SetAllDirty();
            }

            // Legacy Text
            foreach (var text in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>())
            {
                if (text == null || !text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) continue;
                text.text = Translator.Translate(text.text, text.gameObject.name);
            }

            // Legacy 3D TextMesh
            foreach (var mesh in UnityEngine.Object.FindObjectsOfType<UnityEngine.TextMesh>())
            {
                if (mesh == null || !mesh.gameObject.activeInHierarchy || string.IsNullOrEmpty(mesh.text)) continue;
                mesh.text = Translator.Translate(mesh.text, mesh.gameObject.name);
            }
        }

        private void OnDestroy()
        {
            Translator.SaveMissingTranslations();
            _harmony?.UnpatchSelf();
        }
    }
}
