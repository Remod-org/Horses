#region License (GPL v2)
/*
    Horses
    Copyright (c) 2023 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License v2.0.

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
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Rust;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Horses", "RFC1920", "1.0.33")]
    [Description("Manage horse ownership and access")]

    internal class Horses : RustPlugin
    {
        private ConfigData configData;

        [PluginReference]
        private readonly Plugin Friends, Clans, GridAPI, SteamFriends;
        public static Horses Instance;

        // Permanent data for player-owned horses
        private static Dictionary<ulong, List<ulong>> playerhorses = new();
        // Runtime simple list of horses
        private static List<ulong> horses = new();

        private static Dictionary<ulong, HTimer> htimer = new();
        private const string permClaim_Use = "horses.claim";
        private const string permSpawn_Use = "horses.spawn";
        private const string permBreed_Use = "horses.breed";
        private const string permFind_Use = "horses.find";
        private const string permVIP = "horses.vip";
        private bool enabled;
        private bool do1026;

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        #endregion

        #region hooks
        private void LoadData()
        {
            if (do1026)
            {
                Dictionary<ulong, ulong> oldh = Interface.GetMod().DataFileSystem.ReadObject<Dictionary<ulong, ulong>>($"{Name}/ridables");
                foreach (KeyValuePair<ulong, ulong> newh in oldh)
                {
                    if (!playerhorses.ContainsKey(newh.Value))
                    {
                        playerhorses.Add(newh.Value, new List<ulong>());
                    }
                    playerhorses[newh.Value].Add(newh.Key);
                    horses.Add(newh.Key);
                }
                SaveData();
                do1026 = false;
                return;
            }

            playerhorses = Interface.GetMod().DataFileSystem.ReadObject<Dictionary<ulong, List<ulong>>>($"{Name}/ridables");

            foreach (KeyValuePair<ulong, List<ulong>> hl in playerhorses)
            {
                foreach (ulong h in hl.Value)
                {
                    horses.Add(h);
                }
            }
        }

        private void SaveData() => Interface.GetMod().DataFileSystem.WriteObject($"{Name}/ridables", playerhorses);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You are not authorized for this command!",
                ["horseclaimed"] = "You have claimed this horse!",
                ["horselimit"] = "You have reached the limit for claiming horses({0})!",
                ["horsereleased"] = "You have released this horse!",
                ["horsesreleased"] = "You have released all of your horses!",
                ["yourhorse"] = "You have already claimed this horse!",
                ["yourhorse2"] = "Well, hello there.",
                ["horsespawned"] = "You have spawned a horse!",
                ["horseowned"] = "Someone else owns this horse!",
                ["serverowned"] = "Server-owned, free horse.",
                ["horseinfo"] = "{0}:\n  Health: {1}\n  Stamina: {2}\n  Owner: {3}\n  {4}{5}",
                ["hitched"] = "Currently hitched",
                ["forsale"] = "Currently for sale",
                ["breeds"] = "Breeds:\n{0}",
                ["hclaim"] = "Claim a horse",
                ["hrelease"] = "Release a horse you own",
                ["hfind"] = "Locate your horse(s)",
                ["hinfo"] = "Display info about a horse in your view",
                ["hspawn"] = "Spawn a horse",
                ["hremove"] = "Kill your horse",
                ["hbreed"] = "Change horse's breed",
                ["invalidbreed"] = "Invalid breed!",
                ["breed"] = "Current breed is {0}",
                ["breedchanged"] = "Changed breed from {0} to {1}.",
                ["notyourhorse"] = "Someone else owns this horse.  Perhaps no one...",
                ["nohorses"] = "No rideable horses found.",
                ["foundhorse"] = "Your horse is {0}m away in {1}."
            }, this);
        }

        private void OnServerInitialized()
        {
            LoadConfigVariables();
            LoadData();
            enabled = true;
            Instance = this;

            AddCovalenceCommand("hclaim", "CmdClaim");
            AddCovalenceCommand("hrelease", "CmdRelease");
            AddCovalenceCommand("hspawn", "CmdSpawn");
            AddCovalenceCommand("hbreed", "CmdBreed");
            AddCovalenceCommand("hremove", "CmdRemove");
            AddCovalenceCommand("hfind", "CmdFindHorse");
            AddCovalenceCommand("hinfo", "CmdHorseInfo");
            permission.RegisterPermission(permClaim_Use, this);
            permission.RegisterPermission(permFind_Use, this);
            permission.RegisterPermission(permSpawn_Use, this);
            permission.RegisterPermission(permBreed_Use, this);
            permission.RegisterPermission(permVIP, this);

            // Fix ownership for horses perhaps previously claimed but not current managed.
            foreach (RidableHorse horse in UnityEngine.Object.FindObjectsOfType<RidableHorse>())
            {
                if (!horses.Contains(horse.net.ID.Value) && horse.OwnerID != 0)
                {
                    DoLog($"Setting owner of unmanaged horse {horse.net.ID} back to server.");
                    horse.OwnerID = 0;
                }
            }
        }

        private void OnServerShutdown()
        {
            if (configData.Options.EnableTimer)
            {
                // Prevent horse ownership from persisting across reboots if the timeout timer was enabled
                foreach (KeyValuePair<ulong, List<ulong>> data in playerhorses)
                {
                    BaseNetworkable horse = BaseNetworkable.serverEntities.Find(new NetworkableId(data.Key));
                    if (horse != null)
                    {
                        BaseEntity bhorse = horse as BaseEntity;
                        if (bhorse != null) bhorse.OwnerID = 0;
                    }
                }
                playerhorses = new Dictionary<ulong, List<ulong>>();
                SaveData();
            }
        }

        private void OnNewSave()
        {
            playerhorses = new Dictionary<ulong, List<ulong>>();
            SaveData();
        }

        private object OnEntityTakeDamage(RidableHorse horse, HitInfo hitInfo)
        {
            if (!enabled) return null;
            if (horse == null) return null;
            if (hitInfo == null) return null;
            DamageType majority = hitInfo.damageTypes.GetMajorityDamageType();
            if (horses.Contains(horse.net.ID.Value))
            {
                if (horse.IsDead()) return null;
                if (horse.InSafeZone()) return true;

                if (majority == DamageType.Decay)
                {
                    if (configData.Options.AllowDecay) return null;
                    DoLog("Blocking decay damage.");
                    return true;
                }

                DoLog($"{horse.net.ID} owned by {horse.OwnerID} is being attacked!");

                if (configData.Options.AlertWhenAttacked && horse.mountPoints[0].mountable.GetMounted() == null)
                {
                    // Horse visibly and audibly alerts
                    DoLog("Trying to alert!");
                    horse.PlayerServerInput(new InputState() { current = new InputMessage() { buttons = 64 } }, null);
                }

                if (!configData.Options.AllowDamage)
                {
                    DoLog($"{horse.net.ID} damaged blocked");
                    return true;
                }

                if (configData.Options.TCPreventDamage)
                {
                    BuildingPrivlidge tc = horse.GetBuildingPrivilege();
                    if (tc != null)
                    {
                        if (configData.Options.TCMustBeAuthorized)
                        {
                            // Verify horse owner is registered to the TC
                            //foreach (ProtoBuf.PlayerNameID p in tc.authorizedPlayers)
                            foreach (ulong auth in tc.authorizedPlayers)
                            {
                                if (auth == horse.OwnerID)
                                {
                                    // Horse owner is registered to the TC, block damage.
                                    DoLog($"{horse.net.ID} owned by {horse.OwnerID} protected by local TC to which the owner is registered.");
                                    return true;
                                }
                            }
                            // Horse owner is NOT registered to the TC, allow damage.
                            DoLog($"{horse.net.ID} owned by {horse.OwnerID} NOT protected by local TC since the owner is not registered.");
                            return null;
                        }
                        // No local auth required, block damage since we are in BP
                        DoLog($"{horse.net.ID} owned by {horse.OwnerID} protected by local TC.");
                        return true;
                    }
                }
            }
            return null;
        }

        private void OnEntityDeath(RidableHorse entity, HitInfo info)
        {
            if (!enabled) return;
            if (entity == null) return;
            if (horses.Contains(entity.net.ID.Value))
            {
                DoLog($"DeadHorse: {entity.net.ID} owned by {entity.OwnerID}");
                BasePlayer pl = BasePlayer.FindAwakeOrSleeping(entity.OwnerID.ToString());
                if (pl != null)
                {
                    playerhorses[pl.userID].Remove(entity.net.ID.Value);
                }
                horses.Remove(entity.net.ID.Value);
                SaveData();
            }
        }

        private object CanLootEntity(BasePlayer player, RidableHorse horse)
        {
            if (!enabled) return null;
            if (!configData.Options.RestrictStorage) return null;
            if (player == null) return null;

            if (horse != null && horses.Contains(horse.net.ID.Value))
            {
                if (IsFriend(player.userID, horse.OwnerID))
                {
                    DoLog("Horse storage access allowed.");
                    return null;
                }
                Message(player.IPlayer, "horseowned");
                DoLog($"Horse storage access blocked.");
                return true;
            }

            return null;
        }

        //private object OnHorseHitch(RidableHorse2 horse, HitchTrough hitch)
        //{
        //    DoLog("Horse hitch");
        //    if (horse != null && horses.Contains(horse.net.ID))
        //    {
        //        if (IsFriend(hitch.OwnerID, horse.OwnerID))
        //        {
        //            DoLog("Horse hitch allowed.");
        //            return null;
        //        }
        //        DoLog("Horse hitch blocked.");
        //        return true;
        //    }
        //    return null;
        //}

        private object OnHorseLead(RidableHorse horse, BasePlayer player)
        {
            if (!enabled) return null;
            DoLog("Horse lead");
            if (horse != null && horses.Contains(horse.net.ID.Value))
            {
                if (configData.Options.AllowLeadByAnyone) return null;
                if (IsFriend(player.userID, horse.OwnerID))
                {
                    DoLog("Horse lead allowed.");
                    return null;
                }
                Message(player.IPlayer, "horseowned");
                DoLog("Horse lead blocked.");
                return true;
            }
            return null;
        }

        private object CanMountEntity(BasePlayer player, BaseMountable mountable)
        {
            if (!enabled) return null;
            if (player == null) return null;
            if (mountable == null) return null;

            if (!configData.Options.RestrictMounting) return null;
            if (player == null) return null;
            if (mountable == null) return null;

            RidableHorse horse = mountable.GetComponentInParent<RidableHorse>();
            if (horse != null)
            {
                if (horse?.OwnerID == 0) return null;

                if (playerhorses?.Count > 0 && horses.Contains(horse.net.ID.Value))
                {
                    DoLog($"Player {player.userID} wants to mount horse {horse.net.ID}");
                    if (IsFriend(player.userID, horse.OwnerID))
                    {
                        DoLog("Mounting allowed.");
                        return null;
                    }
                    Message(player.IPlayer, "horseowned");
                    DoLog("Mounting blocked.");
                    return true;
                }
            }

            return null;
        }

        private void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            if (!enabled) return;
            if (player == null) return;
            if (!player.userID.IsSteamId()) return;
            if (mountable == null) return;
            if (!configData.Options.SetOwnerOnFirstMount) return;

            RidableHorse horse = mountable.GetComponentInParent<RidableHorse>();

            if (horse != null)
            {
                ulong userid = player.userID;
                if (IsAtLimit(userid) && horse.OwnerID != userid)
                {
                    if (permission.UserHasPermission(userid.ToString(), permVIP))
                    {
                        Message(player.IPlayer, "horselimit", configData.Options.VIPLimit);
                    }
                    else
                    {
                        Message(player.IPlayer, "horselimit", configData.Options.Limit);
                    }
                    return;
                }

                if (horse.OwnerID == userid && configData.Options.ShowWelcomeMessage)
                {
                    Message(player.IPlayer, "yourhorse2");
                }
                else if (!horses.Contains(horse.net.ID.Value) && horse.OwnerID == 0)
                {
                    ClaimHorse(horse, player);
                    DoLog($"Player {player.userID} mounted horse {horse.net.ID} and now owns it.");
                }
                else
                {
                    Message(player.IPlayer, "horseowned");
                }
            }
        }
        #endregion

        public void ClaimHorse(RidableHorse horse, BasePlayer player)
        {
            horse.OwnerID = player.userID;

            ulong horseid = horse.net.ID.Value;
            horses.Remove(horseid);
            if (!playerhorses.ContainsKey(player.userID))
            {
                playerhorses.Add(player.userID, new List<ulong>());
            }
            if (!playerhorses[player.userID].Contains(horseid))
            {
                playerhorses[player.userID].Add(horseid);
            }
            horses.Add(horseid);

            SaveData();

            if (configData.Options.EnableTimer)
            {
                htimer.Add(horseid, new HTimer() { start = Time.realtimeSinceStartup, countdown = configData.Options.ReleaseTime, userid = player.userID });
                HandleTimer(horseid, player.userID, true);
            }
            if (!configData.Options.AllowDecay)
            {
                DecayEntity x = horse.gameObject.GetComponent<DecayEntity>();
                if (x != null)
                {
                    x.enabled = false;
                }
            }
            if (configData.Options.SetHealthOnClaim)
            {
                horse.SetHealth(440);
                horse.ReplenishStamina(30);
            }
            //Homing home = horse.gameObject.GetOrAddComponent<Homing>();
            //home.dohoming = true;

            //Message(player.IPlayer, "horseclaimed");
            SendReply(player, Lang("horseclaimed"));
        }

        #region commands
        [Command("hinfo")]
        private void CmdHorseInfo(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permClaim_Use)) { Message(iplayer, "notauthorized"); return; }

            BasePlayer player = iplayer.Object as BasePlayer;
            List<RidableHorse> hlist = new();
            Vis.Entities(player.transform.position, 1f, hlist);
            foreach (RidableHorse horse in hlist)
            {
                if (horse != null)
                {
                    string owner = horse.OwnerID > 0 ? FindPlayerById(horse.OwnerID) : Lang("serverowned");
                    string breed = horse.GetBreed().breedName.translated + $" ({horse.net.ID})";
                    string hitched = horse.IsHitched() ? Lang("hitched") : "";
                    string forsale = horse.IsForSale ? Lang("forsale") : "";
                    if (!string.IsNullOrEmpty(hitched) && !string.IsNullOrEmpty(forsale))
                    {
                        hitched += ", ";
                    }
                    Message(iplayer, "horseinfo", breed, horse.health, horse.GetStaminaFraction(), owner, hitched, forsale);
                }
            }
        }

        [Command("hfind")]
        private void CmdFindHorse(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permFind_Use)) { Message(iplayer, "notauthorized"); return; }
            bool found = false;
            BasePlayer player = iplayer.Object as BasePlayer;
            if (!playerhorses.ContainsKey(player.userID))
            {
                playerhorses.Add(player.userID, new List<ulong>());
            }
            foreach (ulong h in playerhorses[player.userID])
            {
                found = true;
                BaseEntity entity = BaseNetworkable.serverEntities.Find(new NetworkableId(h)) as BaseEntity;

                string hloc = PositionToGrid(entity.transform.position);
                string dist = Math.Round(Vector3.Distance(entity.transform.position, player.transform.position)).ToString();
                Message(iplayer, "foundhorse", dist, hloc);

                break;
            }
            if (!found) Message(iplayer, "nohorses");
        }

        [Command("hspawn")]
        private void CmdSpawn(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permSpawn_Use)) { Message(iplayer, "notauthorized"); return; }

            BasePlayer player = iplayer.Object as BasePlayer;
            if (IsAtLimit(player.userID))
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

            //const string staticprefab = "assets/rust.ai/nextai/testridablehorse.prefab";
            const string staticprefab = "assets/content/vehicles/horse/ridablehorse.prefab";

            Vector3 spawnpos = player.eyes.position + (player.transform.forward * 2f);
            spawnpos.y = TerrainMeta.HeightMap.GetHeight(spawnpos);
            Vector3 rot = player.transform.rotation.eulerAngles;
            rot = new Vector3(rot.x, rot.y + 180, rot.z);
            BaseEntity horse = GameManager.server.CreateEntity(staticprefab, spawnpos, Quaternion.Euler(rot), true);

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

        [Command("hbreed")]
        private void CmdBreed(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permBreed_Use)) { Message(iplayer, "notauthorized"); return; }
            if (!configData.Options.AllowChangingBreed) return;

            if (args.Length == 0)
            {
                string breedNames = string.Empty;
                for (int i = 0; i < Enum.GetNames(typeof(Breeds)).Length; i++)
                {
                    breedNames += "  " + Enum.GetName(typeof(Breeds), i) + "\n";
                }
                Message(iplayer, "breeds", breedNames);
                return;
            }
            if (args.Length != 1) return;

            List<RidableHorse> hlist = new();
            BasePlayer player = iplayer.Object as BasePlayer;
            Vis.Entities(player.transform.position, 1f, hlist);
            bool found = false;
            foreach (RidableHorse horse in hlist)
            {
                if (horse)
                {
                    found = true;
                    if (horse.OwnerID == player.userID && horses.Contains(horse.net.ID.Value))
                    {
                        string currentBreed = horse.GetBreed().breedName.translated;
                        Breeds breed;
                        bool foundBreed = Enum.TryParse(args[0], true, out breed);
                        if (foundBreed)
                        {
                            horse.ApplyBreed((int)breed);
                            string bname = Enum.GetName(typeof(Breeds), breed);
                            Message(iplayer, "breedchanged", currentBreed, bname);
                            return;
                        }
                        Message(iplayer, "invalidbreed");
                    }
                    else
                    {
                        Message(iplayer, "notyourhorse");
                    }
                }
            }
            if (!found) Message(iplayer, "nohorses");
        }

        [Command("hremove")]
        private void CmdRemove(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permSpawn_Use)) { Message(iplayer, "notauthorized"); return; }

            List<RidableHorse> hlist = new();
            BasePlayer player = iplayer.Object as BasePlayer;
            Vis.Entities(player.transform.position, 1f, hlist);
            bool found = false;
            foreach (RidableHorse horse in hlist)
            {
                if (horse)
                {
                    found = true;
                    if (horse.OwnerID == player.userID && horses.Contains(horse.net.ID.Value))
                    {
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
            if (!iplayer.HasPermission(permClaim_Use)) { Message(iplayer, "notauthorized"); return; }

            BasePlayer player = iplayer.Object as BasePlayer;
            List<RidableHorse> hlist = new();
            Vis.Entities(player.transform.position, 1f, hlist);
            bool found = false;
            foreach (RidableHorse horse in hlist)
            {
                if (horse)
                {
                    found = true;
                    ulong userid = player.userID;
                    if (IsAtLimit(userid))
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

                    if (horse.OwnerID == player.userID)
                    {
                        Message(iplayer, "yourhorse");
                    }
                    else if (!horses.Contains(horse.net.ID.Value) && horse.OwnerID == 0)
                    {
                        ClaimHorse(horse, player);
                        break;
                    }
                    else
                    {
                        Message(iplayer, "horseowned");
                    }
                }
                break;
            }
            if (!found) Message(iplayer, "nohorses");
        }

        [Command("hrelease")]
        private void CmdRelease(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permClaim_Use)) { Message(iplayer, "notauthorized"); return; }
            BasePlayer player = iplayer.Object as BasePlayer;
            bool found = false;

            if (args.Length > 0 && args[0] == "all")
            {
                if (!playerhorses.ContainsKey(player.userID))
                {
                    playerhorses.Add(player.userID, new List<ulong>());
                }
                foreach (ulong horseid in new List<ulong>(playerhorses[player.userID]))
                {
                    RidableHorse horse = BaseNetworkable.serverEntities.Find(new NetworkableId(horseid)) as RidableHorse;
                    if (horse != null)
                    {
                        found = true;
                        horse.OwnerID = 0;
                        playerhorses.Remove(horseid);
                        horses.Remove(horseid);
                        HandleTimer(horseid, player.userID);
                        SaveData();
                    }
                }
                if (found) Message(iplayer, "horsesreleased");
                return;
            }

            List<RidableHorse> hlist = new();
            Vis.Entities(player.transform.position, 1f, hlist);
            foreach (RidableHorse horse in hlist)
            {
                if (horse)
                {
                    found = true;
                    if (horse.OwnerID == player.userID)
                    {
                        horse.OwnerID = 0;
                        ulong horseid = horse.net.ID.Value;
                        if (playerhorses.ContainsKey(player.userID))
                        {
                            playerhorses[player.userID].Remove(horseid);
                        }
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
        [HookMethod("SendHelpText")]
        private void SendHelpText(BasePlayer player)
        {
            StringBuilder sb = new();
            sb.Append("<color=#05eb59>").Append(Name).Append(' ').Append(Version).Append(" - ").Append(Description).Append("</color>\n");
            if (player.IPlayer.HasPermission(permClaim_Use))
            {
                sb.Append("  . ").Append("/hclaim").Append(" - ").Append(Lang("hclaim")).Append("\n");
                sb.Append("  . ").Append("/hrelease").Append(" - ").Append(Lang("hrelease")).Append("\n");
                sb.Append("  . ").Append("/hremove").Append(" - ").Append(Lang("hremove")).Append("\n");
                sb.Append("  . ").Append("/hinfo").Append(" - ").Append(Lang("hinfo")).Append("\n");
            }
            else
            {
                return;
            }
            if (player.IPlayer.HasPermission(permBreed_Use))
            {
                sb.Append("  . ").Append("/hbreed").Append(" - ").Append(Lang("hbreed")).Append("\n");
            }
            if (player.IPlayer.HasPermission(permSpawn_Use))
            {
                sb.Append("  . ").Append("/hspawn").Append(" - ").Append(Lang("hspawn")).Append("\n");
            }

            player.ChatMessage(sb.ToString());
        }


        private static string FindPlayerById(ulong userid)
        {
            foreach (BasePlayer current in BasePlayer.allPlayerList)
            {
                if (current.userID == userid)
                {
                    return current.displayName;
                }
            }
            return "";
        }

        public string PositionToGrid(Vector3 position)
        {
            if (GridAPI != null)
            {
                string[] g = (string[])GridAPI.CallHook("GetGrid", position);
                return string.Concat(g);
            }
            else
            {
                // From GrTeleport
                Vector2 r = new((World.Size / 2) + position.x, (World.Size / 2) + position.z);
                float x = Mathf.Floor(r.x / 146.3f) % 26;
                float z = Mathf.Floor(World.Size / 146.3f) - Mathf.Floor(r.y / 146.3f);

                return $"{(char)('A' + x)}{z - 1}";
            }
        }

        private void DoLog(string message)
        {
            if (configData.Options.debug) Interface.GetMod().LogInfo($"[{Name}] {message}");
        }

        private void PurgeInvalid()
        {
            bool found = false;
            foreach (KeyValuePair<ulong, List<ulong>> hl in new Dictionary<ulong, List<ulong>>(playerhorses))
            {
                found = false;
                foreach (ulong horse in hl.Value)
                {
                    if (BaseNetworkable.serverEntities.Find(new NetworkableId(horse)) == null)
                    {
                        playerhorses[hl.Key]?.Remove(horse);
                        horses?.Remove(horse);
                        found = true;
                    }
                }
                if (found) SaveData();
            }
        }

        private bool IsAtLimit(ulong userid)
        {
            PurgeInvalid();
            if (configData.Options.EnableLimit)
            {
                DoLog($"Checking horse limit for {userid}");
                float amt = 0f;
                if (!playerhorses.ContainsKey(userid))
                {
                    playerhorses.Add(userid, new List<ulong>());
                }
                foreach (ulong horse in playerhorses[userid])
                {
                    DoLog($"Found matching userid {horse}");
                    amt++;
                }
                DoLog($"Player has {amt} horses");
                if (amt > 0 && amt >= configData.Options.VIPLimit && permission.UserHasPermission(userid.ToString(), permVIP))
                {
                    DoLog($"VIP player has met or exceeded the limit of {configData.Options.VIPLimit}");
                    return true;
                }
                if (amt > 0 && amt >= configData.Options.Limit)
                {
                    DoLog($"Non-vip player has met or exceeded the limit of {configData.Options.Limit}");
                    return true;
                }
                DoLog("Player is under the limit.");
                return false;
            }
            DoLog("Limits not enabled.");
            return false;
        }

        private void HandleTimer(ulong horseid, ulong userid, bool start = false)
        {
            if (htimer.ContainsKey(horseid))
            {
                if (start)
                {
                    htimer[horseid].timer = timer.Once(htimer[horseid].countdown, () => HandleTimer(horseid, userid, false));
                    DoLog($"Started release timer for horse {horseid} owned by {userid}");
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
                        BaseNetworkable horse = BaseNetworkable.serverEntities.Find(new NetworkableId(horseid));
                        BasePlayer player = BasePlayer.FindAwakeOrSleeping(userid.ToString());
                        RidableHorse mounted = player.GetMounted().GetComponentInParent<RidableHorse>();

                        if (mounted.net.ID.Value == horseid && configData.Options.ReleaseOwnerOnHorse)
                        {
                            // Player is on this horse and we allow ownership to be removed while on the horse
                            mounted.OwnerID = 0;
                            playerhorses.Remove(horseid);
                            DoLog($"Released horse {horseid} owned by {userid}");
                        }
                        else if (mounted.net.ID.Value == horseid && !configData.Options.ReleaseOwnerOnHorse)
                        {
                            // Player is on this horse and we DO NOT allow ownership to be removed while on the horse
                            // Reset the timer...
                            htimer.Add(horseid, new HTimer() { start = Time.realtimeSinceStartup, countdown = configData.Options.ReleaseTime, userid = userid });
                            htimer[horseid].timer = timer.Once(configData.Options.ReleaseTime, () => HandleTimer(horseid, userid));
                            DoLog($"Reset ownership timer for horse {horseid} owned by {userid}");
                        }
                        else
                        {
                            // Player is NOT mounted on this horse...
                            BaseEntity bhorse = horse as BaseEntity;
                            BasePlayer pl = BasePlayer.FindAwakeOrSleeping(bhorse.OwnerID.ToString());
                            if (pl != null)
                            {
                                playerhorses[pl.userID].Remove(horseid);
                            }
                            horses.Remove(horseid);
                            bhorse.OwnerID = 0;
                            DoLog($"Released horse {horseid} owned by {userid}");
                        }
                        SaveData();
                    }
                    catch
                    {
                        BaseNetworkable horse = BaseNetworkable.serverEntities.Find(new NetworkableId(horseid));
                        BaseEntity bhorse = horse as BaseEntity;
                        bhorse.OwnerID = 0;
                        BasePlayer pl = BasePlayer.FindAwakeOrSleeping(bhorse.OwnerID.ToString());
                        if (pl != null)
                        {
                            playerhorses[pl.userID].Remove(horseid);
                        }
                        horses.Remove(horseid);
                        SaveData();
                        DoLog($"Released horse {horseid} owned by {userid}");
                    }
                }
            }
        }

        private bool IsFriend(ulong playerid, ulong ownerid)
        {
            if (ownerid == playerid) return true;
            if (configData.Options.useFriends && Friends != null)
            {
                object fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if (fr != null && (bool)fr)
                {
                    return true;
                }
            }
            if (configData.Options.useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan = (string)Clans?.CallHook("GetClanOf", ownerid);
                if (playerclan == ownerclan && playerclan != null && ownerclan != null)
                {
                    return true;
                }
            }
            if (configData.Options.useTeams)
            {
                RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerid);
                if (playerTeam?.members.Contains(ownerid) == true)
                {
                    return true;
                }
            }
            if (configData.Options.useSteam && SteamFriends != null)
            {
                List<string> steamFriends = (List<string>)SteamFriends?.CallHook("GetFriends", playerid);
                if (steamFriends.Contains(playerid.ToString()))
                {
                    DoLog($"Steam reports that {playerid} and {ownerid} are Steam friends.");
                    return true;
                }
            }
            return false;
        }

        private enum Breeds
        {
            Appaloosa = 0,
            Bay = 1,
            Buckskin = 2,
            Chestnut = 3,
            Dapple = 4,
            Piebald = 5,
            Pinto = 6,
            Red = 7,
            White = 8,
            Black = 9
        }
        #endregion

        #region config
        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < new VersionNumber(1, 0, 17))
            {
                configData.Options.AllowDecay = false;
                configData.Options.AllowDamage = true;
                configData.Options.TCPreventDamage = true;
                configData.Options.TCMustBeAuthorized = true;
            }
            if (configData.Version < new VersionNumber(1, 0, 18))
            {
                configData.Options.SetHealthOnClaim = true;
            }
            if (configData.Version < new VersionNumber(1, 0, 26))
            {
                do1026 = true;
            }

            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new()
            {
                Options = new Options()
                {
                    useClans = false,
                    useFriends = false,
                    useSteam = false,
                    useTeams = false,
                    debug = false,
                    SetOwnerOnFirstMount = true,
                    ReleaseOwnerOnHorse = false,
                    RestrictMounting = false,
                    RestrictStorage = false,
                    AlertWhenAttacked = false,
                    EnableTimer = false,
                    EnableLimit = true,
                    AllowDecay = false,
                    AllowDamage = true,
                    TCPreventDamage = true,
                    TCMustBeAuthorized = true,
                    ReleaseTime = 600f,
                    Limit = 2f,
                    VIPLimit = 5f
                },
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
            public Options Options;
            public VersionNumber Version;
        }

        public class Options
        {
            public bool useClans;
            public bool useFriends;
            public bool useSteam;
            public bool useTeams;
            public bool debug;
            public bool SetOwnerOnFirstMount;
            public bool ReleaseOwnerOnHorse;
            public bool RestrictMounting;
            public bool RestrictStorage;
            public bool AlertWhenAttacked;
            public bool EnableTimer;
            public bool EnableLimit;
            public bool AllowDecay;
            public bool AllowDamage;
            public bool TCPreventDamage;
            public bool TCMustBeAuthorized;
            public bool AllowLeadByAnyone;
            public bool AllowChangingBreed;
            public bool ShowWelcomeMessage;
            public float ReleaseTime;
            public float Limit;
            public float VIPLimit;
            public bool SetHealthOnClaim;
        }

        public class HTimer
        {
            public Timer timer;
            public float start;
            public float countdown;
            public ulong userid;
        }
        #endregion

        #region Future
        public class Homing : FacepunchBehaviour
        {
            public RidableHorse horse;
            public BasePlayer player;

            public bool dohoming;

            public void Awake()
            {
                horse = GetComponentInParent<RidableHorse>();
                Instance.Puts($"Horse: {horse?.net.ID.Value}");
                player = BasePlayer.Find(horse?.OwnerID.ToString());
                Instance.Puts($"Player: {player?.UserIDString}");
            }

            public void FixedUpdate()
            {
                if (!dohoming) return;
                if (horse?.OwnerID == 0) return;
                if (player == null) return;
                if (Vector3.Distance(horse.transform.position, player.transform.position) > 5f)
                {
                    if (!horse.IsLeading)
                    {
                        Instance.DoLog($"Start leading of {horse.net.ID.Value} by {player?.userID}");
                        horse.SetLeading(player);
                        //BaseEntity.RPCMessage rPCMessage = new()
                        //{
                        //    connection = player.net.connection,
                        //    player = player,
                        //    read = new Network.NetRead()
                        //};
                        //horse.SERVER_Lead(rPCMessage);
                    }
                    return;
                }
                horse.SetLeading(null);
            }
        }
        #endregion
    }
}
