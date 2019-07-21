//AMP FiveM Module - See LICENCE

using Ionic.Zip;
using ModuleShared;
using RCONPlugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FiveMModule
{
    //AppServerBase provides some common functionality for handling application output. It'll automatically discover any methods
    //with the MessageHandler attribute and use them to process messages according to their regex when you use ProcessOutput.
    public class FiveMApp : AppServerBase, IApplicationWrapper, IHasReadableConsole, IHasWriteableConsole
    {
        private readonly ModuleMain module;
        public new AMPProcess ApplicationProcess { get; private set; }

        private const int consoleBackscrollLength = 40;

        public FiveMApp(ModuleMain module) => this.module = module;

        private readonly Dictionary<SupportedOS, string> FXServerAppPath = new Dictionary<SupportedOS, string>()
        {
            { SupportedOS.Windows, "FXServer.exe" }
        };

        private string WorkingDir => Path.Combine(module.settings.FiveM.GamePath, @".\server-data\");
        private string ServerFile => Path.Combine(module.settings.FiveM.GamePath, FXServerAppPath[module.os]);

        public bool IsGameServerInstalled() => File.Exists(ServerFile);

        public bool IsDataPathValid() => Directory.Exists(WorkingDir);

        /// <summary>
        /// Generates a random password for RCON and creates a new ApplicationProcess StartInfo
        /// </summary>
        private void SetupProcess()
        {
            ApplicationProcess = new AMPProcess()
            {
                Win32RequiresConsoleAssistant = false
            };

            RandomRCONPassword = GenerateRandomPassword();

            List<string> Arguments = new List<string>() { };

            Arguments.Add($"+set citizen_dir {new DirectoryInfo($".\\{module.settings.FiveM.GamePath}\\Citizen").FullName}");
            if (!module.settings.FiveM.AnnounceServer)
                Arguments.Add("+set sv_master \"\"");

            Arguments.AddRange(module.settings.FiveM.GetTaggedValues().
               Select(kvp =>
               {
                   if (kvp.Value is bool)
                       if (Convert.ToBoolean(kvp.Value))
                           return $"+set {kvp.Key} 1";
                       else
                           return $"+set {kvp.Key} 0";
                   if (kvp.Value is int)
                       return $"+set {kvp.Key} {kvp.Value.ToString()}";
                   return $"+set {kvp.Key} \"{kvp.Value.ToString()}\"";
               }));

            if (module.settings.FiveM.EndpointPrivacy)
                Arguments.Add("+set sv_endpointprivacy true");

            Arguments.Add($"+set rcon_password {RandomRCONPassword}");

            Arguments.AddRange(module.settings.FiveM.ResourcesToStart.Select(res => $"+start {res}"));
            Arguments.AddRange(module.settings.FiveM.CustomArgs.Select(arg => $"+{arg}"));

            Arguments.Add($"+set tags \"{module.settings.FiveM.ServerTags}\"");
            if (module.settings.FiveM.ServerIcon != "")
                Arguments.Add($"+load_server_icon \"{module.settings.FiveM.ServerIcon}\"");
            Arguments.Add($"+endpoint_add_tcp \"{module.settings.FiveM.EndpointTCP}:{module.settings.FiveM.EndpointTCPPort}\"");
            Arguments.Add($"+endpoint_add_udp \"{module.settings.FiveM.EndpointUDP}:{module.settings.FiveM.EndpointUDPPort}\"");

            ApplicationProcess.StartInfo = new ProcessStartInfo()
            {
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                ErrorDialog = false,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = WorkingDir,
                FileName = ServerFile,
                Arguments = string.Join(" ", Arguments),
            };

            ApplicationProcess.EnableRaisingEvents = true;
            ApplicationProcess.Exited += ApplicationProcess_Exited;
            ApplicationProcess.OutputDataReceived += ApplicationProcess_OutputDataReceived;
            ApplicationProcess.ErrorDataReceived += ApplicationProcess_ErrorDataReceived;
        }

        private void ApplicationProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e) => ProcessMessage(e.Data, "ERROR");

        private void ApplicationProcess_OutputDataReceived(object sender, DataReceivedEventArgs e) => ProcessMessage(e.Data);

        private void ApplicationProcess_Exited(object sender, EventArgs e)
        {
            var unexpectedStop = (State != ApplicationState.Stopping && State != ApplicationState.Restarting);
            var needsRestart = (State == ApplicationState.Restarting);

            State = ApplicationState.Stopped;

            if (unexpectedStop)
            {
                module.log.Warning("The application stopped unexpectedly.");
                UnexpectedStop.InvokeSafe(this, new ServerStoppedEventArgs(DateTime.Now));
            }
            else
            {
                NormalStop.InvokeSafe(this, new ServerStoppedEventArgs(DateTime.Now));
            }

            if (needsRestart)
            {
                Start();
            }
        }

        public void StartGameServer()
        {
            if (!IsGameServerInstalled()) // If our Game Server directory does not exist with the srcds executable in
            {
                try
                {
                    module.log.Debug("FiveM isn't installed... Updating...");
                    Update();
                }
                catch
                {
                    State = ApplicationState.Failed;
                    return;
                }
            }

            State = ApplicationState.PreStart;

            if (!IsDataPathValid())
            {
                module.log.Debug("Data Path Is invalid, Updating data...");
                UpdateData();
            }

            if (!IsGameServerInstalled())
            {
                this.State = ApplicationState.Failed;
                module.log.Warning("FiveM is not installed!");
                return;
            }

            SetupProcess();
            ApplicationProcess.Start();
            ApplicationProcess.BeginOutputReadLine();
            ApplicationProcess.BeginErrorReadLine();
            this.State = ApplicationState.Starting;
            SetupRCON();
        }

        #region RCON

        private QuakeRconClient rcon;

        private async void SetupRCON()
        {
            await Task.Delay(5000);

            if (State != ApplicationState.Starting)
            {
                module.log.Warning("Tried to start RCON but server is not running!");
                return;
            }

            try
            {
                rcon = new QuakeRconClient();
                //rcon.DataRecieved += Rcon_DataRecieved;

                bool connectResult = false;
                int rconRetry = 0;

                //Keep trying to connect to RCON over and over until we either succeed, get a permission denied, or the server stops.
                do
                {
                    module.log.Debug("Tring to connect in RCON...");
                    connectResult = await rcon.Connect("127.0.0.1", 30120);

                    if (connectResult == false)
                    {
                        module.log.Warning($"Connection failed #{rconRetry + 1} attempt");
                        await Task.Delay(10000);
                        rconRetry++;
                    }
                } while (connectResult == false && this.State == ApplicationState.Starting && rconRetry < 10);

                if (!connectResult)
                    throw new Exception("Unable to connect.");

                module.log.Debug("Tring to auth in RCON...");

                var authResult = await rcon.Login(RandomRCONPassword);

                //Once we're connected, the application can transition to the Ready state - even if auth failed (but this stops the console being usable)
                if (connectResult && authResult)
                {
                    module.log.Info("RCON connection successful.");
                    State = ApplicationState.Ready;
                }
                else
                {
                    module.log.Warning("RCON connection failed, console write unavailable.");
                    State = ApplicationState.Ready;
                }
            }
            catch (NotImplementedException)
            {
                module.log.Warning("RCON connection failed, console write unavailable.");
                module.log.Debug("C'mon Mike!");
                State = ApplicationState.Ready;
            }
            catch
            {
                module.log.Warning("RCON connection failed, console write unavailable.");
                State = ApplicationState.Ready;
            }
        }

        #endregion

        #region Message Process
        void ProcessMessage(string message, string type = "Console")
        {
            if (string.IsNullOrEmpty(message)) return;

            var newEntry = new ConsoleEntry()
            {
                Contents = message,
                Source = "Console",
                Type = type,
                Timestamp = DateTime.Now
            };

            if (ConsoleOutputRecieved != null)
            {
                var eventArgs = new CancelableEventArgs<ConsoleEntry>(newEntry);
                ConsoleOutputRecieved(this, eventArgs);

                if (eventArgs.Cancel) return;
            }

            if (!ProcessOutput(message))
            {
                AddConsoleEntry(newEntry);
                module.log.ConsoleOutput(message);
            }

        }

        private new void AddConsoleEntry(ConsoleEntry newEntry)
        {
            consoleLines.Add(newEntry);

            if (consoleLines.Count > consoleBackscrollLength)
                consoleLines.RemoveAt(0);
        }

        [ScheduleableTask("Perform a console command")]
        public void ConsoleCommand(string Command) =>
            PostMessage(Command);
        #endregion

        [ScheduleableTask("Start the FiveM server")]
        public ActionResult Start()
        {
            StartGameServer();
            return ActionResult.Success;
        }

        [ScheduleableTask("Stop the FiveM server")]
        public void Stop() => StopApplication(false);

        public ActionResult Sleep() => throw new NotImplementedException();

        [ScheduleableTask("Restart the FiveM server")]
        public void Restart() => StopApplication(true);

        public void StopApplication(bool andRestart = false)
        {
            if (State != ApplicationState.Stopped)
            {
                State = (andRestart) ? ApplicationState.Restarting : ApplicationState.Stopping;
                ApplicationProcess.Kill();
                State = (andRestart) ? ApplicationState.Restarting : ApplicationState.Stopped;
                AddConsoleEntry(new ConsoleEntry() { Contents = "Server Stopped!", Source = "FiveM Module", Timestamp = DateTime.Now, Type = "Console" });
                module.log.Info("Server Stopped!");
            }
        }

        public void Kill()
        {
            if (State != ApplicationState.Stopped)
                ApplicationProcess.Kill();
        }

        #region Update Things
        public class FiveMVersion
        {
            public FiveMVersion(string url, DateTime release)
            {
                Url = url;
                Release = release;
            }

            public string Url;
            public DateTime Release;

            public override string ToString() => $"URL: {Url}, Release: {Release}";
        }

        public ActionResult Update()
        {
            if (State != ApplicationState.Stopped)
            {
                NormalStop += FiveMApp_AutoUpdate;
                Stop();
                return ActionResult.FailureReason("Stopping instance!");
            }
            else
            {
                UpdateTask();
                return ActionResult.Success;
            }

        }

        private readonly Regex UpdateUrlRegex = new Regex(@".*?(\d+\-[0-9a-z/]+).*?(\d+\-\D+\-\d+\s\d+\:\d+)");

        private async void UpdateTask()
        {
            try
            {
                State = ApplicationState.Installing;

                string page = await new WebClient().DownloadStringTaskAsync(new Uri("https://runtime.fivem.net/artifacts/fivem/build_server_windows/master/"));

                var matches = page.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Where(str => str.Contains("<a href=") && !str.Contains("../") && !str.Contains("revoked/")).Select(str => UpdateUrlRegex.Match(str));

                List<FiveMVersion> Versions = new List<FiveMVersion>();
                foreach (var match in matches)
                    if (match.Success)
                        Versions.Add(new FiveMVersion(match.Groups[1].ToString(), DateTime.Parse(match.Groups[2].ToString())));

                var Releases = Versions.OrderBy(ver => ver.Release).ToArray();

                if (!Directory.Exists(module.settings.FiveM.UpdatesPath))
                    Directory.CreateDirectory(module.settings.FiveM.UpdatesPath);

                string filename = module.settings.FiveM.UpdatesPath + Releases.Last().Url.TrimEnd('/') + ".zip";
                string foldername = module.settings.FiveM.UpdatesPath + Releases.Last().Url.TrimEnd('/');

                var task = module.taskmgr.CreateTask("Downloading server files");

                var succes = await Utilities.DownloadFileWithProgressAsync($"https://runtime.fivem.net/artifacts/fivem/build_server_windows/master/{Releases.Last().Url}server.zip", filename, task);

                if (!succes)
                {
                    module.log.Error("Download failed!");
                    return;
                }

                module.log.Debug("Unziping file...");
                ZipFile ServerZip = new ZipFile(filename);
                await ServerZip.ExtractAllAsync(foldername, ExtractExistingFileAction.OverwriteSilently);

                module.log.Debug("Unzip complete, coping...");

                Move(foldername, module.settings.FiveM.GamePath);

                module.log.Debug("Update complete!");

                State = ApplicationState.Stopped;
            }
            catch (Exception ex)
            {
                module.log.Error($"Failed to update the server version! Exception: {ex}");
            }
        }

        private void Move(string Source, string Destination)
        {
            //Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(Source, "*",
                    SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(Source, Destination));

            //Move all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(Source, "*.*",
                SearchOption.AllDirectories))
            {
                if (File.Exists(newPath.Replace(Source, Destination)))
                    File.Delete(newPath.Replace(Source, Destination));
                File.Move(newPath, newPath.Replace(Source, Destination));
            }
        }

        private async void UpdateData()
        {
            module.log.Debug("Updating server-data...");
            if (!Directory.Exists(module.settings.FiveM.UpdatesPath))
                Directory.CreateDirectory(module.settings.FiveM.UpdatesPath);

            if (!Directory.Exists(Path.Combine(module.settings.FiveM.UpdatesPath, @".\server-data\")))
            {
                module.log.Debug("Downloading cfx-server-data...");

                string zipfilename = Path.Combine(module.settings.FiveM.UpdatesPath, @".\server-data.zip");
                var task = module.taskmgr.CreateTask("Downloading cfx-server-data from Github");
                await Utilities.DownloadFileWithProgressAsync("https://codeload.github.com/citizenfx/cfx-server-data/zip/master", zipfilename, task);

                ZipFile ServerDataZip = new ZipFile(zipfilename);
                await ServerDataZip.ExtractAllAsync(Path.Combine(module.settings.FiveM.UpdatesPath, @".\server-data\"), ExtractExistingFileAction.OverwriteSilently);
            }
            if (!Directory.Exists(module.settings.FiveM.DataPath))
                Directory.CreateDirectory(module.settings.FiveM.DataPath);
            Move(Path.Combine(module.settings.FiveM.UpdatesPath, "server-data", "cfx-server-data-master"), module.settings.FiveM.DataPath);
        }

        void FiveMApp_AutoUpdate(object sender, ServerStoppedEventArgs e)
        {
            NormalStop -= FiveMApp_AutoUpdate;
            Update();
        }
        #endregion

        private new static readonly List<ApplicationState> ValidStates = new List<ApplicationState>() { ApplicationState.Starting, ApplicationState.Ready, ApplicationState.Stopping };
        private readonly Func<ApplicationState, bool> CanGetState = (currentState) => ValidStates.Contains(currentState);

        public new int GetCPUUsage() => (CanGetState(State) && ApplicationProcess != null) ? ApplicationProcess.CPUUsage : 0;

        public new int GetRAMUsage() => (CanGetState(this.State) && ApplicationProcess != null) ? ApplicationProcess.RAMUsageMB : 0;

        public int MaxRAMUsage => 0;

        public int MaxUsers => module.settings.FiveM.MaxPlayers;

        private ApplicationState _State;

        public new ApplicationState State
        {
            get => _State;
            private set
            {
                if (_State != value)
                {
                    var oldValue = _State;
                    _State = value;
                    StateChanged.InvokeSafe(this, new ApplicationStateChangeEventArgs(oldValue, value));
                }
            }
        }

        public new DateTime StartTime => (CanGetState(State) && ApplicationProcess != null) ? ApplicationProcess.StartTime : DateTime.Now;

        public new TimeSpan Uptime => (CanGetState(State) && ApplicationProcess != null) ? new TimeSpan(0) : DateTime.Now.Subtract(StartTime);

#pragma warning disable CS0108 // Member hides inherited member; missing new keyword
        public event EventHandler<ApplicationStateChangeEventArgs> StateChanged;
#pragma warning restore CS0108 // Member hides inherited member; missing new keyword

        public bool SupportsSleep => false;

        public string ApplicationName => "FiveM Dedicated Server";

        public string ModuleName => "FiveM";

        public string ModuleAuthor => "G.Nimrod.G#7286";

        public SupportedOS SupportedOperatingSystems => SupportedOS.Windows;

        public bool CanRunVirtualized => true;

        public bool CanUpdateApplication => true;

        private readonly List<ConsoleEntry> consoleLines = new List<ConsoleEntry>();

        public new IEnumerable<ConsoleEntry> GetEntriesSince(DateTime? Timestamp = null) =>
            consoleLines.Where(cl => cl.Timestamp > (Timestamp ?? DateTime.MinValue));

#pragma warning disable CS0108 // Member hides inherited member; missing new keyword
        public event EventHandler<CancelableEventArgs<ConsoleEntry>> ConsoleOutputRecieved;
#pragma warning restore CS0108 // Member hides inherited member; missing new keyword

        private string RandomRCONPassword = "";

#pragma warning disable 0162
        private string GenerateRandomPassword() =>
#if DEBUG
            "testingpassword123";
#else
        Guid.NewGuid().ToString().Replace("-", "");
#endif
#pragma warning restore 0162

        public void WriteLine(string message)
        {
            if (State != ApplicationState.Stopped && State != ApplicationState.Sleeping)
                PostMessage(message);
        }

        public void WriteLine(string format, params object[] args)
        {
            string Message = (args.Length == 0) ? format : string.Format(format, args);

            if (State != ApplicationState.Stopped && State != ApplicationState.Sleeping)
                PostMessage(Message);
        }

        private void PostMessage(string Message) =>
            //var result = rcon.SendMessage(new SourceRconPacket() { Body = Message, Type = SourceRconPacket.PacketType.ExecCommandOrAuthResponse }, true).Result;
            rcon.SendMessage(Message);

        public class ServerStoppedEventArgs : EventArgs
        {
            public DateTime Time { get; private set; }

            public ServerStoppedEventArgs(DateTime time)
            {
                Time = time;
            }
        }

        [ScheduleableEvent("The FiveM Server stops unexpectedly")]
        public event EventHandler<ServerStoppedEventArgs> UnexpectedStop;

        [ScheduleableEvent("The FiveM Server stops normally")]
        public event EventHandler<ServerStoppedEventArgs> NormalStop;

        public string BaseDirectory
        {
            get => module.settings.FiveM.GamePath;
            set => module.settings.FiveM.GamePath = value;
        }

    }
}
