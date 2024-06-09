using BepInEx;
using BepInEx.Configuration;
using System;
using UnityEngine;
using static UpdateNotifier.AssemblyInfo;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.Collections;
using EFT.UI;

namespace UpdateNotifier
{
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

        public const int TarkovVersion = 29197;
        public const string EscapeFromTarkov = "EscapeFromTarkov.exe";
        public const string ModName = "UpdateNotifier";
        public const string ModVersion = "1.383.01";
        public const string SPTGUID = "com.spt-aki.core";
        public const string SPTVersion = "3.8.0";
    }

    [BepInPlugin("bastudio.updatenotifier", ModName, ModVersion)]
    [BepInDependency(SPTGUID, SPTVersion)]
    [BepInProcess(EscapeFromTarkov)]
    [DefaultExecutionOrder(100)]
    public class UpdateNotifierPlugin : BaseUnityPlugin
    {
        Queue<(BaseUnityPlugin, string)> queue;
        ConfigEntry<bool> Enabled { get; set; }
        ConfigEntry<string> Whitelist { get; set; }
        Coroutine checking;
        void Awake ()
        {
            Config.Bind("Main", "Readme", false, "By enabling the plugin, you agree to allow the plugin to fetch version info from the internet on your behalf, when a mod request to check .");
            Enabled = Config.Bind("Main", "Enabled", false, "By enabling the plugin, you agree to allow the plugin to fetch version info from the internet on your behalf, when a mod request to check .");
            Whitelist = Config.Bind("Main", "Whitelist", "");
        }

        public void CheckForUpdate(BaseUnityPlugin plugin, string versionFileUrl)
        {
            if (!Enabled.Value) return;
            if (queue == null)
                queue = new Queue<(BaseUnityPlugin, string)>();

            queue.Enqueue((plugin, versionFileUrl));

            if (checking == null)
                checking = StartCoroutine(CheckWorker());
        }
        bool inDialog;
        IEnumerator CheckWorker ()
        {
            (BaseUnityPlugin, string) task;
            var wait = new WaitForSeconds(1f);
            while (!PreloaderUI.Instantiated || CommonUI.Instance?.MenuScreen?.isActiveAndEnabled != true)
                yield return wait;
            while (queue.TryDequeue(out task))
            {
                while (inDialog)
                    yield return wait;
                yield return Check(task.Item1, task.Item2);
                yield return wait;
            }
            checking = null;
        }

        IEnumerator Check(BaseUnityPlugin plugin, string versionFileUrl)
        {
            EFT.UI.ConsoleScreen.Log($"[Update Notifier] {plugin.Info.Metadata.Name} requested to check version file at {versionFileUrl}");

            if (!versionFileUrl.StartsWith("https://"))
            {
                string msg = $"Invalid versionFileUrl requested by {plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID}). Version file URL must starts with https://";
                NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Long);
                EFT.UI.ConsoleScreen.LogError(msg);
                yield break;
            }
            if (!versionFileUrl.EndsWith(".update_notifier"))
            {
                string msg = $"Invalid versionFileUrl requested by {plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID}). Version file URL must end with .update_notifier";
                NotificationManagerClass.DisplayWarningNotification(msg, EFT.Communications.ENotificationDurationType.Long);
                EFT.UI.ConsoleScreen.LogError(msg);
                yield break;
            }

            if (!Whitelist.Value.Contains($"{plugin.Info.Metadata.GUID}=>{versionFileUrl}"))
            {
                inDialog = true;
                var msg = $"{plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID}) is requesting to download unknown file from {versionFileUrl} to check for new version, but the url is not in white list.\n\nDo you want to allow the request and add it to whitelist?\n\n This would allows Update Notifier to remind you if it finds a update for the mod from now on.";
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
                    var msg = $"[Update Notifier] Failed to fetch version file requested by {plugin.Info.Metadata.Name}:{webRequest.error}";
                    EFT.UI.ConsoleScreen.LogError(msg);
                    Logger.LogError(msg);
                }
                else
                {
                    if (webRequest.downloadedBytes > 16)
                    {
                        string message = $"[Update Notifier] {plugin.Info.Metadata.Name} requested a suspicious version file! Update Notifier only accepts up to 16 bytes: {webRequest.downloadHandler.text}";
                        EFT.UI.ConsoleScreen.LogWarning(message);
                        yield break;
                    }
                    if (!Version.TryParse(webRequest.downloadHandler.text, out var ver))
                    {
                        string message = $"[Update Notifier] {plugin.Info.Metadata.Name} requested a invalid version file! The content is not a valid version string: {webRequest.downloadHandler.text}";
                        EFT.UI.ConsoleScreen.LogWarning(message);
                        yield break;
                    }
                    else if (ver.CompareTo(plugin.Info.Metadata.Version) < 0)
                    {
                        string message = $"[Update Notifier] Update for {plugin.Info.Metadata.Name} is available: {webRequest.downloadHandler.text} (Current: {plugin.Info.Metadata.Version})";
                        NotificationManagerClass.DisplayWarningNotification(message, EFT.Communications.ENotificationDurationType.Long);
                        EFT.UI.ConsoleScreen.Log(message);
                    }
                }
            }

            void PreAccept ()
            {
                var msg = $"Are you sure you want to allow Update Notifier to fetch the requested version file over the internet?\n\nUpdate Notifier is not responsible in case the url links to malicious servers.\n\n{plugin.Info.Metadata.Name} ({plugin.Info.Metadata.GUID})\n{versionFileUrl}";
                ItemUiContext.Instance.ShowMessageWindow(msg, Accept, Decline, "Update Notifier");
            }

            void Accept ()
            {
                inDialog = false;
                Whitelist.Value += $"{plugin.Info.Metadata.GUID}=>{versionFileUrl}, ";
                CheckForUpdate(plugin, versionFileUrl);
            }

            void Decline ()
            {
                inDialog = false;
                EFT.UI.ConsoleScreen.Log($"[Update Notifier] User cancelled request from {plugin.Info.Metadata.Name}.");
            }
        }
    }
}