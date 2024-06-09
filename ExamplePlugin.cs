using BepInEx;
using BepInEx.Bootstrap;
using System.Collections;

namespace UpdateNotifier
{
    public class ExamplePlugin : BaseUnityPlugin
    {
        const string UPDATE_NOTIFIER_VERSION_FILE_URL = "";
        void Awake ()
        {
            TryCheckUpdate();
        }

        public void TryCheckUpdate ()
        {
            if (!Chainloader.PluginInfos.TryGetValue("bastudio.updatenotifier", out var pluginInfo))
            {
                Logger.LogInfo("Update Notifier not found.");
                return;
            }

            BaseUnityPlugin updntf = pluginInfo.Instance;
            updntf.GetType().GetMethod("CheckForUpdate").Invoke(updntf, new object[] {this, UPDATE_NOTIFIER_VERSION_FILE_URL});
        
        }
    }
}