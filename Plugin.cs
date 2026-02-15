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
        private Rect _windowRect = new Rect(20, 20, 280, 220); // 稍微调大高度
        private float _scanInterval = 1.0f; // 默认 1 秒一次

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

            // 重新引入周期性扫描作为兜底
            if (Time.time - _lastScanTime > _scanInterval) 
            {
                if (Translator.IsEnabled) ScanAllText();
                _lastScanTime = Time.time;
            }

            // 周期性保存缺失翻译
            if (Time.time - _lastSaveTime > 60f)
            {
                Translator.SaveMissingTranslations();
                _lastSaveTime = Time.time;
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

            // 第二行：重载翻译文件
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

            // 第五行：提示信息
            GUILayout.Label("按下F11以显示/隐藏此控制台");

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
                if (tmp == null || !tmp.gameObject.activeInHierarchy || string.IsNullOrEmpty(tmp.text)) continue;
                
                string original = tmp.text;
                string translated = Translator.Translate(original);
                
                if (original != translated)
                {
                    tmp.text = translated;
                }
            }

            // Legacy Text
            foreach (var text in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>())
            {
                if (text == null || !text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) continue;
                text.text = Translator.Translate(text.text);
            }

            // Legacy 3D TextMesh
            foreach (var mesh in UnityEngine.Object.FindObjectsOfType<UnityEngine.TextMesh>())
            {
                if (mesh == null || !mesh.gameObject.activeInHierarchy || string.IsNullOrEmpty(mesh.text)) continue;
                mesh.text = Translator.Translate(mesh.text);
            }
        }

        private void OnDestroy()
        {
            Translator.SaveMissingTranslations();
            _harmony?.UnpatchSelf();
        }
    }
}
