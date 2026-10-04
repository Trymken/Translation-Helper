using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using RPGTALK.Texts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace TranslationHelper
{
    [BepInPlugin("com.trymken.translation_helper", "Translation Helper", "1.0.0")]
    public class TranslationMod : BaseUnityPlugin
    {
        public static string[] trash_text = {"Lorem ipsum dolor sit amet, consectetur adipiscing elit.", "top text goes here\nbottom text goes here", "Test Name", "99/99", "test"};
        public static BlockingCollection<string> _logs_queue = new BlockingCollection<string>();
        public static BlockingCollection<string> _textui_queue = new BlockingCollection<string>();
        public static string translation_path = Path.Combine(Paths.GameRootPath, "TranslationHelper\\translation");
        public static string log_path = Path.Combine(Paths.GameRootPath, "TranslationHelper\\logs");
        public static string dialogs_font_path = Path.Combine(Paths.GameRootPath, "TranslationHelper\\font");
        public static string textui_font_path = Path.Combine(Paths.GameRootPath, "TranslationHelper\\font");
        public static string log_file_path;
        public static string textui_path;
        public static string dialogs_path;
        public static string dump_path = Path.Combine(Paths.GameRootPath, $"TranslationHelper\\dump");
        public static string debug_textui_path = Path.Combine(dump_path, "textui", "textui.txt");
        public static string debug_dialogs_path = Path.Combine(dump_path, "dialogs");
        public static string config_path = Path.Combine(Paths.ConfigPath, "TranslationHelper.cfg");
        public static ConfigFile _config = new ConfigFile(config_path, true);
        public static Dictionary<string, TMP_text_mod> textui_dict = new Dictionary<string, TMP_text_mod>();
        public static Dictionary<string, TMP_text_mod> debug_textui_dict = new Dictionary<string, TMP_text_mod>();
        public static Dictionary<string, TMP_Text> loaded_textui_dict = new Dictionary<string, TMP_Text>();
        public static TMP_FontAsset _font_dialogs = null;
        public static TMP_FontAsset _font_textui = null;

        public static ConfigEntry<bool> _debug_mode;
        public static ConfigEntry<float> _dialog_characters_spacing;
        public static ConfigEntry<bool> _dialog_characters_auto_size;
        public static ConfigEntry<float> _dialog_characters_font_size;
        public static ConfigEntry<bool> _write_log_file;
        public static ConfigEntry<bool> _dialog_log;
        public static ConfigEntry<bool> _create_dialog_files;
        public static ConfigEntry<bool> _create_textui_file;
        public static ConfigEntry<string> _lang;
        public static ConfigEntry<Int16> _max_log_files;
        public static ConfigEntry<string> _custom_font_dialogs;
        public static ConfigEntry<string> _custom_font_textui;

        private void Awake()
        {
            var harmony = new Harmony("com.trymken.translation_helper");
            harmony.PatchAll();

            _debug_mode = _config.Bind("Utility", "debugMode", false, "Enable debug mode. Displays information in the console about captured objects");
            _dialog_log = _config.Bind("Utility", "dialogLog", false, "Enable logging for dialog fields.");
            _write_log_file = _config.Bind("Utility", "writeLogFile", false, "Enable writing a log file with captured objects (debug mode must be enabled).\nThe log file will be created in the TranslationHelper\\logs folder.");
            _max_log_files = _config.Bind<Int16>("Utility", "maxLogFiles", 20, "Maximum number of logs; if the number exceeds this value, the oldest file is overwritten.\nTo remove the limit, set the value to -1 (maximum value: 32,767).");
            _create_dialog_files = _config.Bind("Utility", "createDialogFiles", false, "Enable creating dialog files for translation.\nAll dialogues captured during the process will be saved in a file ready for translation.\nThe files will be created in the TranslationHelper\\dump\\dialogs folder.\nDoes not overwrite files that have already been created.");
            _create_textui_file = _config.Bind("Utility", "createTextUIFile", false, "Enable creating textUI files for translation.\nAll TextMeshPro, TextMeshProUGUI, DisplayMessage captured during the process will be saved in a file ready for translation.\nThe file will be created in the TranslationHelper\\dump\\textui folder.\nDoes not create duplicates, unless their hashes do match.");

            _lang = _config.Bind("General", "lang", "en", "The name of the language folder located at TranslationHelper\\translation\\{lang}");
            _dialog_characters_spacing = _config.Bind("General", "characterSpacing", 0.0f, "Sets the spacing between characters in dialog fields.\nOptimal range: [-60.0, 60.0], depending on the language");
            _dialog_characters_font_size = _config.Bind("General", "characterFontSize", 18.0f, "Sets the font size for characters in dialog fields.");
            _dialog_characters_auto_size = _config.Bind("General", "characterAutoSize", true, "Enable auto size for characters in dialog fields.\nIf there isn't enough space in the text field, it will automatically reduce the character size.");
            _custom_font_dialogs = _config.Bind("General", "customFontDialogs", "default", "This allows you to use your own font in dialog boxes;\nyou need to place the font file in the TranslationHelper\\font directory.\nExample \"umeboshi.ttf\".");
            _custom_font_textui = _config.Bind("General", "customFontTextUI", "default", "This allows you to use your own font in textUI fields;\nyou need to place the font file in the TranslationHelper\\font directory.\nExample \"umeboshi.ttf\"\nFor each interface element, you need to manually enable the font for it in the textui.txt file located at TranslationHelper\\translation\\{lang}\\textui\\textui.txt");

            translation_path = Path.Combine(translation_path, _lang.Value.Trim());
            textui_path = Path.Combine(translation_path, "textui", "textui.txt");
            dialogs_path = Path.Combine(translation_path, "dialogs");

            dialogs_font_path = Path.Combine(dialogs_font_path, _custom_font_dialogs.Value.Trim());
            textui_font_path = Path.Combine(textui_font_path, _custom_font_textui.Value.Trim());

            if (!Directory.Exists(translation_path) && !_lang.Value.Trim().Equals("en"))
            {
                UnityEngine.Debug.LogWarning("[Translation Helper] Translation directory not found");
            }

            Helper.parseTextUIFile(ref textui_dict, textui_path);

            if (_create_textui_file.Value)
            {
                Helper.parseTextUIFile(ref debug_textui_dict, debug_textui_path);
            }

            if (_write_log_file.Value)
            {
                if (!Directory.Exists(log_path))
                {
                    Directory.CreateDirectory(log_path);
                }
                string[] files = Directory.GetFiles(log_path);

                if (files.Length >= _max_log_files.Value)
                {
                    long min = long.MaxValue;
                    string min_file = string.Empty;

                    foreach (string file in files) {
                        long ticks = File.GetCreationTime(file).Ticks;
                        if (ticks < min)
                        {
                            min = ticks;
                            min_file = file;
                        }
                    }
                    File.Delete(min_file);
                }
                log_file_path = Path.Combine(log_path, $"{DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss")}.txt");
            }


            if (_write_log_file.Value)
            {
                Task _write_logs_task = Task.Factory.StartNew(() =>
                    Helper.listenAndWriteToFile(log_file_path, _logs_queue),
                    TaskCreationOptions.LongRunning
                );
            }

            if (_create_textui_file.Value)
            {
                Task _write_textui_task = Task.Factory.StartNew(() =>
                    Helper.listenAndWriteToFile(debug_textui_path, _textui_queue),
                    TaskCreationOptions.LongRunning
                );
            }

            if (_custom_font_dialogs.Value.Trim() != "default" && File.Exists(dialogs_font_path))
            {
                Font custom_font = new Font(dialogs_font_path);
                _font_dialogs = TMP_FontAsset.CreateFontAsset(custom_font);
                if (_font_dialogs != null)
                {
                    _font_dialogs.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                }
            }

            if (_custom_font_textui.Value.Trim() != "default" && File.Exists(textui_font_path)) 
            {
                Font custom_font = new Font(textui_font_path);
                _font_textui = TMP_FontAsset.CreateFontAsset(custom_font);
                if (_font_textui != null)
                {
                    _font_textui.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                }
            }

            Logger.LogInfo("The mod is up and running!");
        }

        private void Update() {

            if (Input.GetKeyDown(KeyCode.F7))
            {
                try
                {
                    Helper.parseTextUIFile(ref textui_dict, textui_path);
                    UnityEngine.Debug.Log("textui refreshed");

                    foreach (var key in TranslationMod.textui_dict.Keys)
                    {
                        if (TranslationMod.loaded_textui_dict.ContainsKey(key) && TranslationMod.loaded_textui_dict[key] != null)
                        {
                            loaded_textui_dict[key].text = TranslationMod.textui_dict[key].text;
                            loaded_textui_dict[key].characterSpacing = TranslationMod.textui_dict[key].char_spacing;
                            loaded_textui_dict[key].enableAutoSizing = TranslationMod.textui_dict[key].auto_sizing;
                            loaded_textui_dict[key].fontSizeMax = TranslationMod.textui_dict[key].font_size;
                            loaded_textui_dict[key].fontSize = TranslationMod.textui_dict[key].font_size;
                            Helper.changeCustomFont(loaded_textui_dict[key], TranslationMod._font_textui, TranslationMod.textui_dict[key].use_custom_font);
                        }
                    }

                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Error refreshing textui: {ex.Message}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(RPGTalk), "NewTalk", new Type[] { })]
    public static class Patch_RPGTalk_NewTalk
    {

        [HarmonyPrefix]
        public static void NewTalk(RPGTalk __instance)
        {
            if (__instance == null || __instance.txtToParse == null) return;
            if (!Directory.Exists(TranslationMod.dialogs_path) || TranslationMod._lang.Value.Trim().Equals("en")) return;

            string asset_name = __instance.txtToParse.name;

            string raw_file_text = __instance.txtToParse.text;
            if (string.IsNullOrEmpty(raw_file_text)) return;

            string file_path = Path.Combine(TranslationMod.dialogs_path, $"{asset_name}.txt");

            if (!File.Exists(file_path)) return;

            string all_text = File.ReadAllText(file_path);

            if (TranslationMod._create_dialog_files.Value)
            {
                if (!Directory.Exists(TranslationMod.debug_dialogs_path))
                {
                    Directory.CreateDirectory(TranslationMod.debug_dialogs_path);
                }
                string debug_file_path = Path.Combine(TranslationMod.debug_dialogs_path, $"{asset_name}.txt");
                if (!File.Exists(debug_file_path))
                {
                    File.WriteAllText(debug_file_path, raw_file_text);
                }
            }

            if (TranslationMod._debug_mode.Value && TranslationMod._dialog_log.Value) {
                UnityEngine.Debug.Log($"[DIALOG] Name: {asset_name}");
                UnityEngine.Debug.Log($"[TEXT]:\n{raw_file_text}");
                UnityEngine.Debug.Log($"[END TEXT]\n");

                if (TranslationMod._write_log_file.Value)
                {
                    TranslationMod._logs_queue.Add($"[DIALOG] Name: {asset_name}\n[TEXT]:\n{raw_file_text}\n[END TEXT]\n\n");
                }
            }

            TextAsset translated_asset = new TextAsset(all_text);
            translated_asset.name = asset_name;
            __instance.txtToParse = translated_asset;

        }
    }


    //[HarmonyPatch(typeof(TMP_Translator), "ChangeTextTo")]
    //public static class Patch_TMP_Translator_ChangeText
    //{
    //    [HarmonyPrefix]
    //    public static void ChangeText(TMP_Translator __instance)
    //    {
    //        if (__instance == null) return;

    //        if (__instance.TMPText != null)
    //        {
    //            Helper.changeCustomFont(__instance.TMPText, TranslationMod._font_dialogs);
    //            __instance.TMPText.characterSpacing = TranslationMod._dialog_characters_spacing.Value;
    //            __instance.TMPText.enableAutoSizing = TranslationMod._dialog_characters_auto_size.Value;
    //            __instance.TMPText.fontSizeMin = 1f;
    //            __instance.TMPText.fontSizeMax = TranslationMod._dialog_characters_font_size.Value;
    //            __instance.TMPText.fontSize = TranslationMod._dialog_characters_font_size.Value;
    //        }
            
    //    }
    //}

    [HarmonyPatch(typeof(TMP_Translator), "Enabled", new Type[] { typeof(bool) })]
    public static class Patch_TMP_Translator_Enabled
    {
        [HarmonyPostfix]
        public static void TranslatorEnabled(TMP_Translator __instance, bool enable)
        {
            if (__instance == null || !enable || __instance.TMPText == null) return;
            Helper.changeCustomFont(__instance.TMPText, TranslationMod._font_dialogs, true);
            __instance.TMPText.characterSpacing = TranslationMod._dialog_characters_spacing.Value;
            __instance.TMPText.enableAutoSizing = TranslationMod._dialog_characters_auto_size.Value;
            __instance.TMPText.fontSizeMin = 1f;
            __instance.TMPText.fontSizeMax = TranslationMod._dialog_characters_font_size.Value;
            __instance.TMPText.fontSize = TranslationMod._dialog_characters_font_size.Value;
        }
    }


    [HarmonyPatch(typeof(TextMeshProUGUI), "Awake")]
    public static class Patch_TMPUGUI_Awake
    {

        [HarmonyPostfix]
        public static void TMPUGUIAwake(TextMeshProUGUI __instance)
        {
            if (__instance == null) return;
            Helper.initTMP(__instance, "TextMeshProUGUI");
        }
    }


    [HarmonyPatch(typeof(TextMeshPro), "Awake")]
    public static class Patch_TMP_Awake
    {

        [HarmonyPostfix]
        public static void TMPAwake(TextMeshPro __instance)
        {
            if (__instance == null) return;
            Helper.initTMP(__instance, "TextMeshPro");
        }
    }


    [HarmonyPatch(typeof(NotificationToast), "Initialize")]
    public static class Patch_DisplayMessage_Awake
    {
        [HarmonyPostfix]
        public static void NotificationToastInitialize(NotificationToast __instance)
        {
            if (__instance == null) return;

            string objectName = __instance.name;
            string sceneName = SceneManager.GetActiveScene().name;
            string hierarchy = Helper.GetSiblingPath(__instance);
            string unique_string = $"{hierarchy}/{__instance.textContent.text}";
            string hash_value = Helper.byteArrayToHex(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(unique_string)));

            Helper.loadTextUI(__instance.textContent, hash_value);
            Helper.changeTextParametrs(__instance.textContent, hash_value);
            Helper.printDebugInfo("DisplayMessage", hierarchy, objectName, sceneName, __instance.GetInstanceID(), __instance.textContent.text, hash_value);
            Helper.writeTextUIFile(hash_value, __instance.textContent.text, __instance.textContent.characterSpacing, __instance.textContent.fontSize, __instance.textContent.enableAutoSizing);
        }

    }

    public static class Helper
    {
        public static string GetSiblingPath(Component component)
        {
            if (component == null) return string.Empty;

            Transform t = component.transform;
            string path = t.gameObject.name;

            while (t != null)
            {
                int index = t.GetSiblingIndex();
                path = path + "/" + index;
                t = t.parent;
            }
            return path;
        }

        public static string byteArrayToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);

            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        public static void parseTextUIFile(ref Dictionary<string, TMP_text_mod> textui_dict, string path) 
        {
            if (!File.Exists(path)) return;

            textui_dict = new Dictionary<string, TMP_text_mod>();
            string pattern = @"\[\s*hash\s*=\s*(?<hash>[a-zA-Z0-9]{32})\s*,\s*auto_sizing\s*=\s*(?<auto_sizing>([Ff][Aa][Ll][Ss][Ee]|[Tt][Rr][Uu][Ee]))\s*,\s*font_size\s*=\s*(?<font_size>\w+(\.?\w*)?)\s*,\s*char_spacing\s*=\s*(?<char_spacing>-?\w+(\.?\w*)?)\s*,\s*custom_font\s*=\s*(?<custom_font>([Ff][Aa][Ll][Ss][Ee]|[Tt][Rr][Uu][Ee]))\s*]\s*:\s*{(?<text>(\s*[^}])*)}";

            string all_content = File.ReadAllText(path);
            MatchCollection matches = Regex.Matches(all_content, pattern, RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                string hash = match.Groups["hash"].Value;
                string text = match.Groups["text"].Value.Trim('\r', '\n');
                bool auto_sizing = bool.Parse(match.Groups["auto_sizing"].Value);
                bool is_custom_font = bool.Parse(match.Groups["custom_font"].Value);
                float font_size = float.Parse(match.Groups["font_size"].Value, CultureInfo.InvariantCulture);
                float char_spacing = float.Parse(match.Groups["char_spacing"].Value, CultureInfo.InvariantCulture);

                if (!textui_dict.ContainsKey(hash))
                {
                    textui_dict[hash] = new TMP_text_mod(text, char_spacing, font_size, auto_sizing, is_custom_font);
                }
            }
        }

        public static void writeTextUIFile(string hash_value, string text, float char_spacing, float font_size, bool auto_size) 
        {
            if (TranslationMod._create_textui_file.Value && !TranslationMod.debug_textui_dict.ContainsKey(hash_value))
            {
                string textui_folder = Path.Combine(TranslationMod.dump_path, "textui");
                if (!Directory.Exists(textui_folder))
                {
                    Directory.CreateDirectory(textui_folder);
                }
                string textui_file_path = Path.Combine(textui_folder, $"textui.txt");
                if (!File.Exists(textui_file_path))
                {
                    File.WriteAllText(textui_file_path, string.Empty);
                }

                if (!Helper.isTrashText(text))
                {
                    TranslationMod._textui_queue.Add(FormattableString.Invariant($"[hash={hash_value}, auto_sizing={auto_size}, font_size={font_size}, char_spacing={char_spacing}, custom_font={false}] : {{\n{text}\n}}\n\n"));
                    TranslationMod.debug_textui_dict[hash_value] = new TMP_text_mod(text, char_spacing, font_size, auto_size, false);
                }
            }
        }

        public static void printDebugInfo(string real_obj_name, string hierarchy, string obj_name, string scene_name, int unique_id, string text, string hash) 
        {
            if (TranslationMod._debug_mode.Value)
            {
                UnityEngine.Debug.Log($"[{real_obj_name}] Name: {obj_name}, SCENE: {scene_name}, ID: {unique_id}, HIERARCHY: {hierarchy}");
                UnityEngine.Debug.Log($"[HASH MD5/Translation key]: {hash}");
                UnityEngine.Debug.Log($"[TEXT]: {text}\n");

                if (TranslationMod._write_log_file.Value)
                {
                    TranslationMod._logs_queue.Add($"[{real_obj_name}] Name: {obj_name}, SCENE: {scene_name}, ID: {unique_id}, HIERARCHY: {hierarchy}\n[HASH MD5/Translation key]: {hash}\n[TEXT]:\n{text}\n[END TEXT]\n\n");
                }
            }
        }

        public static void changeTextParametrs(TMP_Text __instance, string hash_value)
        {
            if (TranslationMod.textui_dict.ContainsKey(hash_value))
            {
                TMP_text_mod temp_t = TranslationMod.textui_dict[hash_value];
                Helper.changeCustomFont(__instance, TranslationMod._font_textui, temp_t.use_custom_font);
                __instance.characterSpacing = temp_t.char_spacing;
                __instance.enableAutoSizing = temp_t.auto_sizing;
                __instance.fontSizeMin = 1f;
                __instance.fontSizeMax = temp_t.font_size;
                __instance.fontSize = temp_t.font_size;
                __instance.text = temp_t.text;
            }
        }

        public static void initTMP(TMP_Text __instance, string real_obj_name) 
        {
            string objectName = __instance.name;
            string sceneName = SceneManager.GetActiveScene().name;
            string hierarchy = Helper.GetSiblingPath(__instance);
            string unique_string = $"{hierarchy}/{__instance.text}";
            string hash_value = Helper.byteArrayToHex(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(unique_string)));

            Helper.loadTextUI(__instance, hash_value);
            Helper.changeTextParametrs(__instance, hash_value);
            Helper.printDebugInfo(real_obj_name, hierarchy, objectName, sceneName, __instance.GetInstanceID(), __instance.text, hash_value);
            Helper.writeTextUIFile(hash_value, __instance.text, __instance.characterSpacing, __instance.fontSize, __instance.enableAutoSizing);
        }

        public static void loadTextUI(TMP_Text instance, string hash_value)
        {
            TranslationMod.loaded_textui_dict[hash_value] = instance;
        }

        public static void listenAndWriteToFile(string file_path, BlockingCollection<string> queue)
        {
            using (FileStream fs = new FileStream(file_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096, FileOptions.WriteThrough))
            using (StreamWriter writer = new StreamWriter(fs))
            {
                foreach (var message in queue.GetConsumingEnumerable())
                {
                    writer.Write(message);
                    writer.Flush();
                }
            }
        }

        public static bool changeCustomFont(TMP_Text __instance, TMP_FontAsset font, bool use_custom_font)
        {
            if (font != null && use_custom_font)
            {
                __instance.font = font;
                return true;
            }
            return false;
        }

        public static bool isTrashText(string text)
        {
            foreach (string trash in TranslationMod.trash_text)
            {
                if (text.StartsWith(trash))
                    return true;
            }
            return false;
        }
    }

    public struct TMP_text_mod
    {
        public string text;
        public float char_spacing;
        public float font_size;
        public bool auto_sizing;
        public bool use_custom_font;

        public TMP_text_mod(string text, float char_spacing, float font_size, bool auto_sizing, bool is_custom_font)
        {
            this.text = text;
            this.char_spacing = char_spacing;
            this.font_size = font_size;
            this.auto_sizing = auto_sizing;
            this.use_custom_font = is_custom_font;
        }
    }
}
