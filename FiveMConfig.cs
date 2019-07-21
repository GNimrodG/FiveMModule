//AMP FiveM Module - See LICENCE

using ModuleShared;
using System.Collections.Generic;
using System.ComponentModel;

namespace FiveMModule
{
    public class FiveMConfig : SettingStore
    {
        [Description("FiveM")]
        public class FiveMSettings : SettingSectionStore
        {
            public string GamePath = "./FiveM/";

            public string UpdatesPath = "./Updates/";

            public string DataPath = "./FiveM/server-data/";

            [WebSetting("Max players", "Server player slot limit (must be between 1 and 32)", false, "sv_maxclients")]
            public int MaxPlayers = 32;


            [WebSetting("License key", "Get it from: <a href=\"https://keymaster.fivem.net/\">https://keymaster.fivem.net/</a>", false, "sv_licenseKey")]
            public string LicenseKey = "";

            [WebSetting("Enable Scripthook", "", false, "sv_scriptHookAllowed")]
            public bool ScriptHook = true;

            [WebSetting("HostName", "Set your server's hostname", false, "sv_hostname")]
            public string Hostname = "FiveM - by AMP";

            [WebSetting("Announce server", "Display the server on the server list", false)]
            public bool AnnounceServer = true;

            [WebSetting("Endpoint Privacy", "Hide player endpoints in external log output", false)]
            public bool EndpointPrivacy = true;

            [WebSetting("Resources to start", "", false)]
            public List<string> ResourcesToStart = new List<string>() { "sessionmanager", "mapmanager", "chat", "spawnmanager", "sessionmanager", "fivem", "hardcap", "rconlog", "scoreboard", "playernames" };

            [WebSetting("Server Tags", "A comma-separated list of tags for your server", false)]
            public string ServerTags = "default";

            [WebSetting("Server Icon", "File path", false)]
            public string ServerIcon = "";

            [WebSetting("Endpoint TCP", "TCP Endpoint IP", false)]
            public string EndpointTCP = "0.0.0.0";

            [WebSetting("Endpint TCP Port", "TCP Endpoint Port", false)]
            public int EndpointTCPPort = 30120;

            [WebSetting("Endpoint UDP", "UDP Endpoint IP", false)]
            public string EndpointUDP = "0.0.0.0";

            [WebSetting("Endpint UDP Port", "UDP Endpoint Port", false)]
            public int EndpointUDPPort = 30120;

            [WebSetting("Custom startup arguments", "Like the lines in the server.cfg", false)]
            public List<string> CustomArgs = new List<string>() {
                "add_ace group.admin command allow",
                "add_ace group.admin command.quit deny",
                "add_principal identifier.steam:110000112345678 group.admin"
            };
        }

        public FiveMSettings FiveM = new FiveMSettings();
    }
}
