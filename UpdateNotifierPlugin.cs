using BepInEx;
using System;
using static UpdateNotifier.AssemblyInfo;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Concurrent;
using BepInEx.Configuration;
using UnityEngine;
using EFT.UI;
using UnityEngine.Networking;

namespace UpdateNotifier
{
    public struct QueuedCheck
    {
        public readonly string name;
        public readonly string id;
        public readonly Version version;
        public readonly string remote;

        public QueuedCheck(string name, string id, Version version, string remote)
        {
            this.name = name;
            this.id = id;
            this.version = version;
            this.remote = remote;
        }
    }

    public static class AssemblyInfo
    {
        public const string Title = ModName;
        public const string Description = "";
        public const string Configuration = SPTVersion;
        public const string Company = "";
        public const string Product = ModName;
        public const string Copyright = "Copyright © 2024 BA";
        public const string Trademark = "";
        public const string Culture = "";

        public const int TarkovVersion = 33420;
        public const string EscapeFromTarkov = "EscapeFromTarkov.exe";
        public const string ModName = "UpdateNotifier";
        public const string ModVersion = "1.3100.0";
        public const string SPTGUID = "com.SPT.core";
        public const string SPTVersion = "3.10.0";
    }

    [BepInPlugin("bastudio.updatenotifier", ModName, ModVersion)]
    [BepInDependency(SPTGUID, SPTVersion)]
    [BepInProcess(EscapeFromTarkov)]
    public class UpdateNotifierPlugin : BaseUnityPlugin
    {
        ConcurrentQueue<QueuedCheck> queue;
        ConfigEntry<bool> Enabled { get; set; }
        ConfigEntry<string> Whitelist { get; set; }
        Coroutine checking;
        int updatesAvailable;
        void Awake ()
        {
            Config.Bind("Main", "Readme", false, "By enabling the plugin, you agree to allow the plugin to fetch version info from the internet on your behalf, when a mod request to check .");
            Enabled = Config.Bind("Main", "Enabled", false, "By enabling the plugin, you agree to allow the plugin to fetch version info from the internet on your behalf, when a mod request to check .");
            Whitelist = Config.Bind("Main", "Whitelist", "");

            running = CheckSourceFiles();
            CheckForUpdate(this, "https://raw.githubusercontent.com/No3371/SPT_UpdateNotifier/master/.update_notifier");
        }

        void FixedUpdate ()
        {
            if (CommonUI.Instance?.MenuScreen?.isActiveAndEnabled == true
             && queue != null &&!queue.IsEmpty)
            {
                
                if (checking == null)
                    checking = StartCoroutine(CheckWorker());
            }
        }

        public void CheckForUpdate(BaseUnityPlugin plugin, string versionFileUrl)
        {
            if (!Enabled.Value) return;
            if (queue == null)
                queue = new ();

            queue.Enqueue(new QueuedCheck(plugin.Info.Metadata.Name, plugin.Info.Metadata.GUID, plugin.Info.Metadata.Version, versionFileUrl));

            if (checking == null)
                checking = StartCoroutine(CheckWorker());
        }
        public void CheckForUpdate(string name, string id, Version version, string remote)
        {
            if (!Enabled.Value) return;
            if (queue == null)
                queue = new ();

            queue.Enqueue(new QueuedCheck(name, id, version, remote));

            if (checking == null)
                checking = StartCoroutine(CheckWorker());
        }
        public void QueueCheckForUpdate(string name, string id, Version version, string remote)
        {
            if (!Enabled.Value) return;
            if (queue == null)
                queue = new ();

            queue.Enqueue(new QueuedCheck(name, id, version, remote));
        }
        bool inDialog;
        IEnumerator CheckWorker ()
        {
            QueuedCheck task;
            var wait = new WaitForSeconds(1f);
            while (!PreloaderUI.Instantiated || CommonUI.Instance?.MenuScreen?.isActiveAndEnabled != true)
                yield return wait;

            while (queue.TryDequeue(out task))
            {
                while (inDialog)
                    yield return wait;
                yield return Check(task.name, task.id, task.version, task.remote);
                yield return wait;
            }

            if (updatesAvailable > 0)
            {
                string message = $"[Update Notifier] Updates available for {updatesAvailable} mods. Please check console. (~)";
                NotificationManagerClass.DisplayWarningNotification(message, EFT.Communications.ENotificationDurationType.Long);
            }
            // EFT.UI.ConsoleScreen.Log(message);
            checking = null;
        }

        IEnumerator Check(string name, string id, Version version, string versionFileUrl)
        {
            EFT.UI.ConsoleScreen.Log($"[Update Notifier] {name} requested to check version file at {versionFileUrl}");

            if (!versionFileUrl.StartsWith("https://"))
            {
                string msg = $"Invalid versionFileUrl requested by {name}. Version file URL must starts with https://";
                NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Long);
                EFT.UI.ConsoleScreen.LogError(msg);
                yield break;
            }
            if (!versionFileUrl.EndsWith(".update_notifier"))
            {
                string msg = $"Invalid versionFileUrl requested by {name}. Version file URL must end with .update_notifier";
                NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Long);
                EFT.UI.ConsoleScreen.LogError(msg);
                yield break;
            }

            if (!Whitelist.Value.Contains($"{id}=>{versionFileUrl}"))
            {
                inDialog = true;
                var msg = $"{name} ({id}) is requesting to download unknown file from {versionFileUrl} to check for new version, but the url is not in your white list.\n\nDo you want to allow the request and add it to whitelist?\n\n This would allows Update Notifier to remind you if it finds a update for the mod from now on.";
                ItemUiContext.Instance.ShowMessageWindow(msg, PreAccept, Decline, "Update Notifier");
                yield break;
            }

            using (UnityWebRequest webRequest = UnityWebRequest.Get(versionFileUrl))
            {
                webRequest.timeout = 3;
                // Request and wait for the desired page.
                yield return webRequest.SendWebRequest();

                if (webRequest.isNetworkError || webRequest.isHttpError)
                {
                    var msg = $"[Update Notifier] Failed to fetch version file requested by {name}:{webRequest.error}";
                    EFT.UI.ConsoleScreen.LogError(msg);
                    Logger.LogError(msg);
                }
                else
                {
                    var text = webRequest.downloadHandler.text;
                    if (webRequest.downloadedBytes > 16)
                    {
                        string message = $"[Update Notifier] {name} ({id}) requested a suspicious version file! Update Notifier only accepts up to 16 bytes: {text}";
                        EFT.UI.ConsoleScreen.LogWarning(message);
                        yield break;
                    }
                    if (!Version.TryParse(text, out var ver))
                    {
                        string message = $"[Update Notifier] {name} ({id}) requested a invalid version file! The content is not a valid version string: {text} ({text.Length})";
                        EFT.UI.ConsoleScreen.LogWarning(message);
                        yield break;
                    }
                    else if (ver.CompareTo(version) > 0)
                    {
                        string message = $"[Update Notifier] Update for {name} is available: {text} (Current: {version})";
                        // NotificationManagerClass.DisplayWarningNotification(message, EFT.Communications.ENotificationDurationType.Long);
                        EFT.UI.ConsoleScreen.Log(message);
                        updatesAvailable++;
                    }
                }
            }

            void PreAccept ()
            {
                var msg = $"Are you sure you want to allow Update Notifier to fetch the requested version file over the internet?\n\nUpdate Notifier is not responsible in case the url links to malicious servers. A malicious server may collect your IP or sort like how ad companies do.\n\nRequesting:\n\n{name} ({id})\n{versionFileUrl}";
                ItemUiContext.Instance.ShowMessageWindow(msg, Accept, Decline, "Update Notifier");
            }

            void Accept ()
            {
                inDialog = false;
                Whitelist.Value += $"{id}=>{versionFileUrl}, ";
                CheckForUpdate(name, id, version, versionFileUrl);
            }

            void Decline ()
            {
                inDialog = false;
                EFT.UI.ConsoleScreen.Log($"[Update Notifier] User cancelled request from {name} ({id}).");
            }
        }

        Task running;
        public Task CheckSourceFiles ()
        {
            if (running != null && running.IsCompleted == false)
                return running;
            running = Task<IEnumerable<string>>.Run(() => {
                List<(string, string)> loaded = new List<(string, string)>();
                IEnumerable<string> serverMods = Directory.EnumerateDirectories($"{BepInEx.Paths.GameRootPath}/user/mods");
                foreach (var dir in serverMods)
                {
                    string srcFile = Path.Combine(dir, ".update_notifier_source");
                    if (!File.Exists(srcFile))
                        continue;

                    string v = File.ReadAllText(srcFile);
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        var message = $"[Update Notifier] Invalid .update_notifier_source: { srcFile }";
                        Logger.LogError(message);
                        continue;
                    }
                    loaded.Add((srcFile, v));
                    Logger.LogInfo($"[Update Notifier] Found source file: {srcFile}");
                }

                foreach (var src in loaded)
                {
                    var packageJson = File.ReadAllText(Path.Combine(Path.GetDirectoryName(src.Item1), "package.json"));
                    ServerPackage package = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerPackage>(packageJson);
                    if (package == null)
                    {
                        var message = $"[Update Notifier] Invalid package.json: { packageJson }";
                        Logger.LogError(message);
                        continue;
                    }
                    package.remote = src.Item2;
                    if (!Version.TryParse(package.version, out var ver))
                    {
                        EFT.UI.ConsoleScreen.Log($"[Update Notifier] Invalid version string: {package.version} ({src.Item1})");
                        continue;
                    }
                    QueueCheckForUpdate(package.name, $"{package.author}.{package.name}", ver, package.remote);
                }
                
            }).ContinueWith(t => {
                running = null;
                if (t.IsFaulted) Logger.LogError(t.Exception.Flatten());
            });

            return running;
        }

    }
    public class ServerPackage
    {
        public string name { get; set; }
        public string author   { get; set; }
        public string version { get; set; }
        public string remote { get; set; }
    }
}