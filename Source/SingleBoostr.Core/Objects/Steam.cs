﻿using Steam4NET;
using SteamWebAPI2.Interfaces;
using System;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Steam.Models.SteamCommunity;
using System.Collections.Generic;
using System.Threading.Tasks;
using SingleBoostr.Core.Misc;
using System.Text;
using System.Security.Cryptography;

namespace SingleBoostr.Core.Objects
{
    public class Steam
    {
        public ISteam006 Steam006 { get; private set; }
        public IClientUser ClientUser { get; private set; }
        public ISteamApps001 SteamApps001 { get; private set; }
        public ISteamApps006 SteamApps006 { get; private set; }
        public IClientEngine ClientEngine { get; private set; }
        public ISteamUser017 SteamUser017 { get; private set; }
        public ISteamClient017 SteamClient017 { get; private set; }
        public ISteamFriends002 SteamFriends002 { get; private set; }
        public ISteamFriends015 SteamFriends015 { get; private set; }

        public PlayerService APIPlayerService { get; private set; } = null;
        public Thread APIThread { get; private set; } = null;
        public List<SteamApp> APPS = new List<SteamApp> { };

        public TSteamError steamError = new TSteamError();
        public TimeSpan APILastFetch { get; private set; } = TimeSpan.FromTicks(DateTime.Now.Ticks);
        public TimeSpan APIPlaytimeLastFetch { get; private set; } = TimeSpan.FromTicks(DateTime.Now.Ticks);

        private int User { get; set; } = 0;
        private int Pipe { get; set; } = 0;
        private string APIAuthKey { get; set; } = "";
        public string APIKey
        {
            private get => APIAuthKey;
            set
            {
                APIAuthKey = value;
                if (APIkeyValid && !APIConnected) APIPlayerService = new PlayerService(APIAuthKey);
            }
        }
        public List<ulong> Devs = new List<ulong>() {
            76561199109931625
        };

        /// <summary>
        /// 
        /// </summary>
        /// <param name="apikey"></param>
        /// <param name="appID"></param>
        public Steam(string apikey = "", uint appID = 0)
        {
            //setup API
            APIKey = apikey;

            //setup app to idle
            if (appID > 0) RegisterAppID(appID);

            //Create API Thread too periodically fetch data from steam
            APIThread = new Thread(async () =>
            {
                Thread.CurrentThread.IsBackground = true;
                var firstload = true;
                Console.WriteLine("API Thread: Started");
                while (true) firstload = await APIFetch(firstload);
            });
        }

        #region Auto Methods
        public bool APIkeyValid => !string.IsNullOrEmpty(APIAuthKey) && !string.IsNullOrWhiteSpace(APIAuthKey);
        public bool APIConnected => APIkeyValid && APIPlayerService != null;
        public bool APICanFetch => TimeSpan.FromTicks(DateTime.Now.Ticks) >= APILastFetch.Add(TimeSpan.FromSeconds(15));//API can fetch every 15 secs
        public bool APICanFetchPlaytime => APICanFetch && TimeSpan.FromTicks(DateTime.Now.Ticks) >= APIPlaytimeLastFetch.Add(TimeSpan.FromMinutes(5));//API can fetch playtime every 5 mins
        public string DisplayName => Utils.GetUnicodeString(SteamFriends015.GetPersonaName());
        public ulong Steam64ID => SteamUser017.GetSteamID().ConvertToUint64(); // SteamUser015.GetSteamID().ConvertToUint64();
        public string BEGuid => SteamIDToBEGuid(Steam64ID);
        public string ProfileUrl => $"http://steamcommunity.com/profiles/{Steam64ID}";
        public string ProfileBadgeUrl => $"{ProfileUrl}/badges";
        public EPersonaState ActiveState
        {
            get => SteamFriends015.GetPersonaState();
            set
            {
                SteamFriends002.SetPersonaState(value);
            }
        }
        public bool Online => ActiveState.HasFlag(EPersonaState.k_EPersonaStateOnline);
        public bool Offline => ActiveState.HasFlag(EPersonaState.k_EPersonaStateOffline);
        public bool Away => ActiveState.HasFlag(EPersonaState.k_EPersonaStateAway) || ActiveState.HasFlag(EPersonaState.k_EPersonaStateSnooze);
        public bool LookingToTrade => (Online || Away) && ActiveState.HasFlag(EPersonaState.k_EPersonaStateLookingToTrade);
        public bool LookingToPlay => (Online || Away) && ActiveState.HasFlag(EPersonaState.k_EPersonaStateLookingToPlay);

        #endregion
         

        #region Methods()
        public async Task<bool> Connect()
        {
            //Idler
            if (APPS.Count > 0) foreach (var APP in APPS) await APP.Start();

            //API
            if (APIThread != null) APIThread.Start();

            //Steamworks
            if (!Steamworks.Load(true)) return false; 
            if (Environment.CurrentDirectory == Steamworks.GetInstallPath()) return false;
             
            Steam006 = Steamworks.CreateSteamInterface<ISteam006>();
            if (Steam006.Startup(0, ref steamError) == 0) return false;

            SteamClient017 = Steamworks.CreateInterface<ISteamClient017>();
            if (SteamClient017 == null) return false;

            ClientEngine = Steamworks.CreateInterface<IClientEngine>();
            if (ClientEngine == null) return false;

            Pipe = SteamClient017.CreateSteamPipe();
            if (Pipe == 0) return false;

            User = SteamClient017.ConnectToGlobalUser(Pipe);
            if (User == 0 || User == -1) return false;

            SteamUser017 = SteamClient017.GetISteamUser<ISteamUser017>(User, Pipe);
            ClientUser = ClientEngine.GetIClientUser<IClientUser>(User, Pipe);
            SteamApps001 = SteamClient017.GetISteamApps<ISteamApps001>(User, Pipe);
            SteamApps006 = SteamClient017.GetISteamApps<ISteamApps006>(User, Pipe);
            SteamFriends002 = SteamClient017.GetISteamFriends<ISteamFriends002>(User, Pipe);
            SteamFriends015 = SteamClient017.GetISteamFriends<ISteamFriends015>(User, Pipe);
             
            return SteamUser017 != null
                && ClientUser != null
                && SteamApps006 != null
                && SteamFriends002 != null
                && SteamFriends015 != null;
        }
        public async Task Disconnect(bool terminate = true)
        {
            //Stop idler
            if (APPS.Count > 0) foreach (var APP in APPS) await APP.Stop();

            //Stop API
            if (APIThread != null) APIThread.Abort();

            //Kill App
            if (terminate) Environment.Exit(-1);
        }
        private async Task<bool> APIFetch(bool fastfetch)
        {
            //API ready and games loaded
            if (APIConnected && APPS.Any())
            {
                //Fetch playtime
                if (APICanFetchPlaytime || (fastfetch && APICanFetch))
                {
                    foreach (SteamApp APP in APPS)
                    {
                        Console.WriteLine("API Thread: Fetching Playtime");

                        //get user game info
                        var game = GetGameInfo(APP.ID);

                        if (game != null)
                        {
                            //set playtime
                            var playtime = Math.Round(game.PlaytimeForever.TotalHours, 0);
                            if (APP.Playtime != playtime) APP.Playtime = playtime;
                        }

                        APIPlaytimeLastFetch = TimeSpan.FromTicks(DateTime.Now.Ticks);
                        fastfetch = false;
                    }
                }
            }

            await Task.Delay(5 * 1000);

            return fastfetch;
        }
        public IReadOnlyCollection<OwnedGameModel> UserGames()
        {
            APILastFetch = TimeSpan.FromTicks(DateTime.Now.Ticks);
            return APIPlayerService.GetOwnedGamesAsync(Steam64ID, true, true).GetAwaiter().GetResult().Data.OwnedGames;
        }
        public bool Callback(ref CallbackMsg_t callbackMsg) => Steamworks.GetCallback(Pipe, ref callbackMsg);
        public bool FreeCallback() => Steamworks.FreeLastCallback(Pipe);
        public OwnedGameModel GetGameInfo(uint appID) => UserGames().Where(g => g.AppId == appID).First();
        public bool IsFriend(CSteamID friendId) => friendId != Steam64ID ? SteamFriends015.HasFriend(friendId, (int)EFriendFlags.k_EFriendFlagNone) : false;
        public bool AddFriend(string emailOrAccountName) => !string.IsNullOrEmpty(emailOrAccountName) ? SteamFriends002.AddFriendByName(emailOrAccountName) > -1 : false;
        public bool AddFriend(CSteamID senderId) => !IsFriend(senderId) ? SteamFriends002.AddFriend(senderId) : false;
        public string GetFriendName(CSteamID friendId) => IsFriend(friendId) ? SteamFriends015.GetFriendPersonaName(friendId) : DisplayName;
        public async Task AddDevsAsFriend()
        {
            foreach (ulong Dev in Devs)
            {
                if (!AddFriend(Dev)) Console.WriteLine($"Unable to add developer: {GetFriendName(Dev)} | {Dev}");
                await Task.Delay(1 * 1000);
            }
        }
        public bool RemoveFriend(CSteamID senderId) => senderId != Steam64ID ? SteamFriends002.RemoveFriend(senderId) : false;
        public bool SendFriendMessage(CSteamID receiver, string message) => SteamFriends002.SendMsgToFriend(receiver, Steam4NET.EChatEntryType.k_EChatEntryTypeChatMsg, Encoding.UTF8.GetBytes(message));
        public int GetMessage(CSteamID senderId, int chatID, byte[] data, ref EChatEntryType type) => SteamFriends002.GetChatMessage(senderId, chatID, data, ref type);
        public bool RegisterAppID(uint appID)
        {
            var APP = new SteamApp(this, appID);
            if (APP.IDSet)
            {
                APPS.Add(APP);
                return true;
            }
            return false;
        }
        public async Task<uint> ParseAppID(string appID)
        {
            if (string.IsNullOrEmpty(appID) || !uint.TryParse(appID, out uint AppID)) return 0;
            await Task.Delay(50);
            return AppID;
        }
        public string SteamIDToBEGuid(CSteamID steamID)
        {
            using var md5 = MD5.Create();
            var bytes = new List<byte>();
            var beGuid = new StringBuilder();
            var starter = Encoding.ASCII.GetBytes("BE");

            long index;
            for (index = 0; index < starter.Length; index++) bytes.Add(starter[index]);
            for (index = 0; index < 8; index++)
            {
                bytes.Add((byte)(steamID & 0xFF));
                steamID >>= 8;
            }

            var hash = md5.ComputeHash(bytes.ToArray());

            for (index = 0; index < hash.Length; index++) beGuid.Append(hash[index].ToString("X2"));
            return beGuid.ToString();
        }
        public string ProfileBadgeUrlPage(int page = 1) => $"{ProfileBadgeUrl}/?p={page}";
        public bool IsAppOwned(uint appID) => SteamApps006.BIsSubscribedApp(appID);

        #endregion
    }
}