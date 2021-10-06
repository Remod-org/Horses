#region License (GPL v3)
/*
    Horses
    Copyright (c) 2021 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Game.Rust;
using System;

namespace Oxide.Plugins
{
    [Info("Horses", "RFC1920", "1.0.10")]
    [Description("Manage horse ownership and access")]

    class Horses : RustPlugin
    {
        private ConfigData configData;

        [PluginReference]
        private readonly Plugin Friends, Clans, GridAPI;

        // horseid, playerid
        private static Dictionary<ulong, ulong> horses = new Dictionary<ulong, ulong>();
        private static Dictionary<ulong, HTimer> htimer = new Dictionary<ulong, HTimer>();
        private const string permClaim_Use = "horses.claim";
        private const string permSpawn_Use = "horses.spawn";
        private const string permFind_Use  = "horses.find";
        private const string permVIP       = "horses.vip";

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        #endregion

        #region hooks
        private void LoadData() => horses = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, ulong>>($"{Name}/ridables");
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject($"{Name}/ridables", horses);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You are not authorized for this command!",
                ["horseclaimed"] = "You have claimed this horse!",
                ["horselimit"] = "You have reached the limit for claiming horses({0})!",
                ["horsereleased"] = "You have released this horse!",
                ["yourhorse"] = "You have already claimed this horse!",
                ["horsespawned"] = "You have spawned a horse!",
                ["horseowned"] = "Someone else owns this horse!",
                ["notyourhorse"] = "Someone else owns this horse.  Perhaps no one...",
                ["nohorses"] = "No rideable horses found.",
                ["foundhorse"] = "Your horse is {0}m away in {1}."
            }, this);
        }

        private void Init()
        {
            LoadConfigVariables();
            LoadData();

            AddCovalenceCommand("hclaim", "CmdClaim");
            AddCovalenceCommand("hrelease", "CmdRelease");
            AddCovalenceCommand("hspawn", "CmdSpawn");
            AddCovalenceCommand("hremove", "CmdRemove");
            AddCovalenceCommand("hfind", "CmdFindHorse");
            permission.RegisterPermission(permClaim_Use, this);
            permission.RegisterPermission(permFind_Use, this);
            permission.RegisterPermission(permSpawn_Use, this);
            permission.RegisterPermission(permVIP, this);
        }

        private void OnServerShutdown()
        {
            if(configData.Options.EnableTimer)
            {
                // Prevent horse ownership from persisting across reboots if the timeout timer was enabled
                foreach(var data in horses)
                {
                    var horse = BaseNetworkable.serverEntities.Find((uint)data.Key);
                    if (horse != null)
                    {
                        (horse as BaseEntity).OwnerID = 0;
                    }
                }
                horses = new Dictionary<ulong, ulong>();
                SaveData();
            }
        }

        private void OnNewSave()
        {
            horses = new Dictionary<ulong, ulong>();
            SaveData();
        }

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity == null) return null;
            if (entity is RidableHorse)
            {
                if (horses.ContainsKey(entity.net.ID))
                {
                    if (configData.Options.debug) Puts($"Horse: {entity.net.ID} owned by {entity.OwnerID} is being attacked!");
                    if (configData.Options.AlertWhenAttacked)
                    {
                        var horse = entity as RidableHorse;
                        if (horse.mountPoints[0].mountable.GetMounted() == null)
                        {
                            InputMessage message = new InputMessage() { buttons = 64 };
                            horse.RiderInput(new InputState() { current = message }, null);
                        }
                    }
                }
            }
            return null;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return;
            if (entity is RidableHorse)
            {
                if (configData.Options.debug) Puts($"DeadHorse: {entity.net.ID} owned by {entity.OwnerID}");
                if (horses.ContainsKey(entity.net.ID))
                {
                    horses.Remove(entity.net.ID);
                    SaveData();
                }
            }
        }

        object CanMountEntity(BasePlayer player, RidableHorse mountable)
        {
            if (!configData.Options.RestrictMounting) return null;
            if (player == null) return null;
            var horse = mountable.GetComponentInParent<RidableHorse>() ?? null;
            if (horse != null)
            {
                if (configData.Options.debug) Puts($"Player {player.userID.ToString()} wants to mount horse {mountable.net.ID.ToString()}");
                if (horses.ContainsKey(mountable.net.ID))
                {
                    if (horse.OwnerID == player.userID || IsFriend(player.userID, horse.OwnerID))
                    {
                        if (configData.Options.debug) Puts("Mounting allowed.");
                        return null;
                    }
                    if (configData.Options.debug) Puts("Mounting blocked.");
                }
            }

            return null;
        }

        private void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            if (player == null) return;
            if (mountable == null) return;
            if (!configData.Options.SetOwnerOnFirstMount == true) return;

            var horse = mountable.GetComponentInParent<RidableHorse>() ?? null;
            if (horse != null)
            {
                if (!horses.ContainsKey(mountable.net.ID))
                {
                    horse.OwnerID = player.userID;

                    ulong horseid = (horse as BaseMountable).net.ID;
                    horses.Remove(horseid);
                    horses.Add(horseid, player.userID);
                    SaveData();
                    if (configData.Options.EnableTimer)
                    {
                        htimer.Add(horseid, new HTimer() { start = Time.realtimeSinceStartup, countdown = configData.Options.ReleaseTime, userid = player.userID });
                        HandleTimer(horseid, player.userID, true);
                    }
                    Message(player.IPlayer, "horseclaimed");
                    if (configData.Options.debug) Puts($"Player {player.userID.ToString()} mounted horse {mountable.net.ID.ToString()} and now owns it.");
                }
            }
        }
        #endregion

        #region commands
        [Command("hfind")]
        private void CmdFindHorse(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permFind_Use)) { Message(iplayer, "notauthorized"); return; }
            bool found = false;

            foreach (var h in horses)
            {
                if (h.Value == Convert.ToUInt64(iplayer.Id))
                {
                    found = true;
                    BasePlayer player = iplayer.Object as BasePlayer;
                    BaseEntity entity = BaseNetworkable.serverEntities.Find((uint)h.Key) as BaseEntity;

                    string hloc = PositionToGrid(entity.transform.position);
                    string dist = Math.Round(Vector3.Distance(entity.transform.position, player.transform.position)).ToString();
                    Message(iplayer, "foundhorse", dist, hloc);

                    break;
                }
            }
            if (!found) Message(iplayer, "nohorses");
        }

        [Command("hspawn")]
        private void CmdSpawn(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permSpawn_Use)) { Message(iplayer, "notauthorized"); return; }

            if(HandleLimit(Convert.ToUInt64(iplayer.Id)))
            {
                if (iplayer.HasPermission(permVIP))
                {
                    Message(iplayer, "horselimit", configData.Options.VIPLimit);
                }
                else
                {
                    Message(iplayer, "horselimit", configData.Options.Limit);
                }
                return;
            }

            var player = iplayer.Object as BasePlayer;
            string staticprefab = "assets/rust.ai/nextai/testridablehorse.prefab";

            Vector3 spawnpos = player.eyes.position + player.transform.forward * 2f;
            spawnpos.y = TerrainMeta.HeightMap.GetHeight(spawnpos);
            Vector3 rot = player.transform.rotation.eulerAngles;
            rot = new Vector3(rot.x, rot.y + 180, rot.z);
            var horse = GameManager.server.CreateEntity(staticprefab, spawnpos, Quaternion.Euler(rot), true);

            if (horse)
            {
                horse.Spawn();
                if (iplayer.HasPermission(permClaim_Use))
                {
                    CmdClaim(iplayer, "hclaim", null);
                }
                Message(iplayer, "horsespawned");
            }
            else
            {
                horse.Kill();
            }
        }

        [Command("hremove")]
        private void CmdRemove(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permSpawn_Use)) { Message(iplayer, "notauthorized"); return; }

            List<RidableHorse> hlist = new List<RidableHorse>();
            Vis.Entities((iplayer.Object as BasePlayer).transform.position, 1f, hlist);
            bool found = false;
            foreach(var horse in hlist)
            {
                if (horse as RidableHorse)
                {
                    found = true;
                    if (horse.OwnerID == (iplayer.Object as BasePlayer).userID && horses.ContainsKey(horse.net.ID))
                    {
//                        horses.Remove(horse.net.ID); // Handled by OnEntityDeath()
                        horse.Hurt(500);
                    }
                    else
                    {
                        Message(iplayer, "notyourhorse");
                    }
                }
            }
            if (!found) Message(iplayer, "nohorses");
        }

        [Command("hclaim")]
        private void CmdClaim(IPlayer iplayer, string command, string[] args)
        {
           // if (configData.Options.SetOwnerOnFirstMount) return;
            if (!iplayer.HasPermission(permClaim_Use)) { Message(iplayer, "notauthorized"); return; }

            List<RidableHorse> hlist = new List<RidableHorse>();
            Vis.Entities((iplayer.Object as BasePlayer).transform.position, 1f, hlist);
            bool found = false;
            foreach(var horse in hlist)
            {
                if (horse as RidableHorse)
                {
                    found = true;
                    ulong userid = (iplayer.Object as BasePlayer).userID;
                    if (HandleLimit(userid))
                    {
                        if (iplayer.HasPermission(permVIP))
                        {
                            Message(iplayer, "horselimit", configData.Options.VIPLimit);
                        }
                        else
                        {
                            Message(iplayer, "horselimit", configData.Options.Limit);
                        }
                        return;
                    }

                    if (horse.OwnerID == 0)
                    {
                        ulong horseid = (horse as BaseMountable).net.ID;
                        horse.OwnerID = userid;
                        horses.Remove(horseid);
                        horses.Add(horseid, horse.OwnerID);
                        SaveData();
                        Message(iplayer, "horseclaimed");
                        if (configData.Options.EnableTimer)
                        {
                            htimer.Add(horseid, new HTimer() { start = Time.realtimeSinceStartup, countdown = configData.Options.ReleaseTime, userid = horse.OwnerID });
                            HandleTimer(horseid, horse.OwnerID, true);
                        }
                        break;
                    }
                    else if (horse.OwnerID == (iplayer.Object as BasePlayer).userID)
                    {
                        Message(iplayer, "yourhorse");
                    }
                    else
                    {
                        Message(iplayer, "horseowned");
                    }
                }
            }
            if (!found) Message(iplayer, "nohorses");
        }

        [Command("hrelease")]
        private void CmdRelease(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permClaim_Use)) { Message(iplayer, "notauthorized"); return; }

            List<RidableHorse> hlist = new List<RidableHorse>();
            Vis.Entities((iplayer.Object as BasePlayer).transform.position, 1f, hlist);
            bool found = false;
            foreach(var horse in hlist)
            {
                if (horse as RidableHorse)
                {
                    found = true;
                    if (horse.OwnerID == (iplayer.Object as BasePlayer).userID)
                    {
                        horse.OwnerID = 0;
                        ulong horseid = (horse as BaseMountable).net.ID;
                        horses.Remove(horseid);
                        HandleTimer(horseid, horse.OwnerID);
                        SaveData();
                        Message(iplayer, "horsereleased");
                        break;
                    }
                    else
                    {
                        Message(iplayer, "notyourhorse");
                    }
                }
            }
            if (!found) Message(iplayer, "nohorses");
        }
        #endregion

        #region helpers
        public string PositionToGrid(Vector3 position)
        {
            if (GridAPI != null)
            {
                var g = (string[]) GridAPI.CallHook("GetGrid", position);
                return string.Join("", g);
            }
            else
            {
                // From GrTeleport
                var r = new Vector2(World.Size / 2 + position.x, World.Size / 2 + position.z);
                var x = Mathf.Floor(r.x / 146.3f) % 26;
                var z = Mathf.Floor(World.Size / 146.3f) - Mathf.Floor(r.y / 146.3f);

                return $"{(char)('A' + x)}{z - 1}";
            }
        }

        private bool HandleLimit(ulong userid)
        {
            if(configData.Options.EnableLimit)
            {
                float amt = 0f;
                foreach(var horse in horses)
                {
                    if(horse.Value == userid)
                    {
                        amt++;
                    }
                }
                if (amt >= configData.Options.VIPLimit && permission.UserHasPermission(userid.ToString(), permVIP))
                {
                    return true;
                }
                if (amt >= configData.Options.Limit)
                {
                    return true;
                }
            }
            return false;
        }
        private void HandleTimer(ulong horseid, ulong userid, bool start=false)
        {
            if(htimer.ContainsKey(horseid))
            {
                if(start)
                {
                    htimer[horseid].timer = timer.Once(htimer[horseid].countdown, () => { HandleTimer(horseid, userid, false); });
                    if (configData.Options.debug) Puts($"Started release timer for horse {horseid.ToString()} owned by {userid.ToString()}");
                }
                else
                {
                    if (htimer.ContainsKey(horseid))
                    {
                        htimer[horseid].timer.Destroy();
                        htimer.Remove(horseid);
                    }

                    try
                    {
                        var horse = BaseNetworkable.serverEntities.Find((uint)horseid);
                        var player = RustCore.FindPlayerByIdString(userid.ToString());
                        var mounted = player.GetMounted().GetComponentInParent<RidableHorse>() ?? null;

                        if (mounted.net.ID == horseid && configData.Options.ReleaseOwnerOnHorse)
                        {
                            // Player is on this horse and we allow ownership to be removed while on the horse
                            mounted.OwnerID = 0;
                            horses.Remove(horseid);
                            if (configData.Options.debug) Puts($"Released horse {horseid.ToString()} owned by {userid.ToString()}");
                        }
                        else if (mounted.net.ID == horseid && !configData.Options.ReleaseOwnerOnHorse)
                        {
                            // Player is on this horse and we DO NOT allow ownership to be removed while on the horse
                            // Reset the timer...
                            htimer.Add(horseid, new HTimer() { start = Time.realtimeSinceStartup, countdown = configData.Options.ReleaseTime, userid = userid });
                            htimer[horseid].timer = timer.Once(configData.Options.ReleaseTime, () => { HandleTimer(horseid, userid); });
                            if (configData.Options.debug) Puts($"Reset ownership timer for horse {horseid.ToString()} owned by {userid.ToString()}");
                        }
                        else
                        {
                            // Player is NOT mounted on this horse...
                            (horse as BaseEntity).OwnerID = 0;
                            horses.Remove(horseid);
                            if (configData.Options.debug) Puts($"Released horse {horseid.ToString()} owned by {userid}");
                        }
                        SaveData();
                    }
                    catch
                    {
                        var horse = BaseNetworkable.serverEntities.Find((uint)horseid);
                        (horse as BaseEntity).OwnerID = 0;
                        horses.Remove(horseid);
                        SaveData();
                        if (configData.Options.debug) Puts($"Released horse {horseid.ToString()} owned by {userid}");
                    }
                }
            }
        }

        // playerid = active player, ownerid = owner of camera, who may be offline
        bool IsFriend(ulong playerid, ulong ownerid)
        {
            if(configData.Options.useFriends && Friends != null)
            {
                var fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if(fr != null && (bool)fr)
                {
                    return true;
                }
            }
            if(configData.Options.useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan  = (string)Clans?.CallHook("GetClanOf", ownerid);
                if(playerclan == ownerclan && playerclan != null && ownerclan != null)
                {
                    return true;
                }
            }
            if(configData.Options.useTeams)
            {
                BasePlayer player = BasePlayer.FindByID(playerid);
                if(player.currentTeam != 0)
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                    if(playerTeam == null) return false;
                    if(playerTeam.members.Contains(ownerid))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        #endregion

        #region config
        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }
        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            var config = new ConfigData
            {
                Version = Version
            };
            SaveConfig(config);
        }
        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private class ConfigData
        {
            public Options Options = new Options();
            public VersionNumber Version;
        }

        private class Options
        {
            public bool useClans = false;
            public bool useFriends = false;
            public bool useTeams = false;
            public bool debug = false;
            public bool SetOwnerOnFirstMount = true;
            public bool ReleaseOwnerOnHorse = false;
            public bool RestrictMounting = false;
            public bool AlertWhenAttacked = false;
            public bool EnableTimer = false;
            public bool EnableLimit = true;
            public float ReleaseTime = 600f;
            public float Limit = 2f;
            public float VIPLimit = 5f;
        }

        public class HTimer
        {
            public Timer timer;
            public float start;
            public float countdown;
            public ulong userid;
        }
        #endregion
    }
}
