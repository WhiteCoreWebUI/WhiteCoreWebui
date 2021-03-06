/*
 * Copyright (c) Contributors, http://WhiteCore-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the WhiteCore-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using WhiteCore.Framework;
using WhiteCore.Framework.ClientInterfaces;
using WhiteCore.Framework.ConsoleFramework;
using WhiteCore.Framework.DatabaseInterfaces;
using WhiteCore.Framework.Modules;
using WhiteCore.Framework.SceneInfo;
using WhiteCore.Framework.Servers;
using WhiteCore.Framework.Servers.HttpServer;
using WhiteCore.Framework.Servers.HttpServer.Implementation;
using WhiteCore.Framework.Servers.HttpServer.Interfaces;
using WhiteCore.Framework.Services;
using WhiteCore.Framework.Services.ClassHelpers.Assets;
using WhiteCore.Framework.Services.ClassHelpers.Profile;
using WhiteCore.Framework.Utilities;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.Imaging;
using OpenMetaverse.StructuredData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using DataPlugins = WhiteCore.Framework.Utilities.DataManager;
using FriendInfo = WhiteCore.Framework.Services.FriendInfo;
using GridRegion = WhiteCore.Framework.Services.GridRegion;
using RegionFlags = WhiteCore.Framework.Services.RegionFlags;

namespace WhiteCore.Addon.WebUI
{
    public class WebUIHandler : IService
    {
        public IHttpServer m_server = null;
        public IHttpServer m_server2 = null;
        string m_servernick = "hippogrid";
        protected IRegistryCore m_registry;
        public string Name
        {
            get { return GetType().Name; }
        }

        #region IService

        public void Initialize(IConfigSource config, IRegistryCore registry)
        {
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
            if (config.Configs["GridInfoService"] != null)
                m_servernick = config.Configs["GridInfoService"].GetString("gridnick", m_servernick);
            m_registry = registry;
            IConfig handlerConfig = config.Configs["Handlers"];
            string name = handlerConfig.GetString("WebUIHandler", "");
            if (name != Name)
                return;
            string Password = handlerConfig.GetString("WebUIHandlerPassword", String.Empty);
            if (Password != "")
            {
                IConfig gridCfg = config.Configs["GridInfoService"];
                OSDMap gridInfo = new OSDMap();
                if (gridCfg != null)
                {
                    if (gridCfg.GetString("gridname", "") != "" && gridCfg.GetString("gridnick", "") != "")
                    {
                        foreach (string k in gridCfg.GetKeys())
                        {
                            gridInfo[k] = gridCfg.GetString(k);
                        }
                    }
                }

                m_server = registry.RequestModuleInterface<ISimulationBase>().GetHttpServer(handlerConfig.GetUInt("WebUIHandlerPort"));
                //This handler allows sims to post CAPS for their sims on the CAPS server.
                m_server.AddStreamHandler(new WebUIHTTPHandler(Password, registry, gridInfo, UUID.Zero));
                m_server2 = registry.RequestModuleInterface<ISimulationBase>().GetHttpServer(handlerConfig.GetUInt("WebUITextureServerPort"));
                m_server2.AddStreamHandler(new GenericStreamHandler("GET", "/index.php?method=GridTexture", OnHTTPGetTextureImage));
                gridInfo["WebUITextureServer"] = m_server2.ServerURI;

                MainConsole.Instance.Commands.AddCommand("webui promote user", "Grants the specified user administrative powers within webui.", "webui promote user", PromoteUser, false, true);
                MainConsole.Instance.Commands.AddCommand("webui demote user", "Revokes administrative powers for webui from the specified user.", "webui demote user", DemoteUser, false, true);
                MainConsole.Instance.Commands.AddCommand("webui add user", "Deprecated alias for webui promote user.", "webui add user", PromoteUser, false, true);
                MainConsole.Instance.Commands.AddCommand("webui remove user", "Deprecated alias for webui demote user.", "webui remove user", DemoteUser, false, true);
            }
        }

        public void FinishedStartup()
        {
        }

        #endregion

        #region textures

        public byte[] OnHTTPGetTextureImage(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            if (httpRequest.QueryString.Get("method") != "GridTexture")
                return MainServer.BlankResponse;

            MainConsole.Instance.Debug("[WebUI]: Sending image jpeg");
            byte[] jpeg = new byte[0];
            IAssetService m_AssetService = m_registry.RequestModuleInterface<IAssetService>();
            IJ2KDecoder m_j2kDecoder = m_registry.RequestModuleInterface<IJ2KDecoder>();

            MemoryStream imgstream = new MemoryStream();
            Image mapTexture = null;
            imgstream = new MemoryStream();

            try
            {
                // Taking our jpeg2000 data, decoding it, then saving it to a byte array with regular jpeg data
                AssetBase mapasset = m_AssetService.Get(httpRequest.QueryString.Get("uuid"));

                // Decode image to System.Drawing.Image
                mapTexture = m_j2kDecoder.DecodeToImage(mapasset.Data);
                if (mapTexture == null)
                    return jpeg;
                // Save to bitmap

                mapTexture = ResizeBitmap(mapTexture, 128, 128);
                EncoderParameters myEncoderParameters = new EncoderParameters();
                myEncoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);

                // Save bitmap to stream
                mapTexture.Save(imgstream, GetEncoderInfo("image/jpeg"), myEncoderParameters);



                // Write the stream to a byte array for output
                jpeg = imgstream.ToArray();
            }
            catch (Exception)
            {
                // Dummy!
                MainConsole.Instance.Warn("[WebUI]: Unable to post image.");
            }
            finally
            {
                // Reclaim memory, these are unmanaged resources
                // If we encountered an exception, one or more of these will be null
                if (mapTexture != null)
                    mapTexture.Dispose();

                if (imgstream != null)
                {
                    imgstream.Close();
                    imgstream.Dispose();
                }
            }

            httpResponse.ContentType = "image/jpeg";
            return jpeg;
        }

        private Bitmap ResizeBitmap(Image b, int nWidth, int nHeight)
        {
            Bitmap newsize = new Bitmap(nWidth, nHeight);
            Graphics temp = Graphics.FromImage(newsize);
            temp.DrawImage(b, 0, 0, nWidth, nHeight);
            temp.SmoothingMode = SmoothingMode.AntiAlias;
            temp.DrawString(m_servernick, new Font("Arial", 8, FontStyle.Regular), new SolidBrush(Color.FromArgb(90, 255, 255, 50)), new Point(2, 115));

            return newsize;
        }

        // From msdn
        private static ImageCodecInfo GetEncoderInfo(String mimeType)
        {
            ImageCodecInfo[] encoders;
            encoders = ImageCodecInfo.GetImageEncoders();
            for (int j = 0; j < encoders.Length; ++j)
            {
                if (encoders[j].MimeType == mimeType)
                    return encoders[j];
            }
            return null;
        }

        #endregion

        #region Console Commands

        private void PromoteUser (IScene scene, string[] cmd)
        {
            string name = MainConsole.Instance.Prompt ("Name of user");
            UserAccount acc = m_registry.RequestModuleInterface<IUserAccountService> ().GetUserAccount(null, name);
            if (acc == null)
            {
                MainConsole.Instance.Warn ("You must create the user before promoting them.");
                return;
            }
            IAgentConnector agents = DataPlugins.RequestPlugin<IAgentConnector>();
            if (agents == null)
            {
                MainConsole.Instance.Warn("Could not get IAgentConnector plugin");
                return;
            }
            IAgentInfo agent = agents.GetAgent (acc.PrincipalID);
            if (agent == null)
            {
                MainConsole.Instance.Warn("Could not get IAgentInfo for " + name + ", try logging the user into your grid first.");
                return;
            }
            agent.OtherAgentInformation["WebUIEnabled"] = true;
            DataPlugins.RequestPlugin<IAgentConnector>().UpdateAgent(agent);
            MainConsole.Instance.Warn ("Admin added");
        }

        private void DemoteUser(IScene scene, string[] cmd)
        {
            string name = MainConsole.Instance.Prompt ("Name of user");
            UserAccount acc = m_registry.RequestModuleInterface<IUserAccountService> ().GetUserAccount(null, name);
            if (acc == null)
            {
                MainConsole.Instance.Warn ("User does not exist, no action taken.");
                return;
            }
            IAgentConnector agents = DataPlugins.RequestPlugin<IAgentConnector>();
            if (agents == null)
            {
                MainConsole.Instance.Warn("Could not get IAgentConnector plugin");
                return;
            }
            IAgentInfo agent = agents.GetAgent(acc.PrincipalID);
            if (agent == null)
            {
                MainConsole.Instance.Warn("Could not get IAgentInfo for " + name + ", try logging the user into your grid first.");
                return;
            }
            agent.OtherAgentInformation["WebUIEnabled"] = false;
            DataPlugins.RequestPlugin<IAgentConnector>().UpdateAgent(agent);
            MainConsole.Instance.Warn ("Admin removed");
        }

        #endregion
    }

    public class WebUIHTTPHandler : BaseRequestHandler, IStreamedRequestHandler
    {
        protected string m_password;
        protected IRegistryCore m_registry;
        protected OSDMap GridInfo;
        private UUID AdminAgentID;
        private Dictionary<string, MethodInfo> APIMethods = new Dictionary<string, MethodInfo>();

        public WebUIHTTPHandler(string pass, IRegistryCore reg, OSDMap gridInfo, UUID adminAgentID) : 
            base("POST", "/WEBUI")
        {
            m_registry = reg;
            m_password = Util.Md5Hash(pass);
            GridInfo = gridInfo;
            AdminAgentID = adminAgentID;
            MethodInfo[] methods = this.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (uint i = 0; i < methods.Length; ++i)
            {
                if (methods[i].IsPrivate && methods[i].ReturnType == typeof(OSDMap) && methods[i].GetParameters().Length == 1 && methods[i].GetParameters()[0].ParameterType == typeof(OSDMap))
                {
                    APIMethods[methods[i].Name] = methods[i];
                }
            }
        }

        #region BaseStreamHandler

        public override byte[] Handle(string path, Stream requestData,
                OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            string body = HttpServerHandlerHelpers.ReadString(requestData).Trim();

            MainConsole.Instance.TraceFormat("[WebUI]: query String: {0}", body);
            string method = string.Empty;
            OSDMap resp = new OSDMap();
            try
            {
                OSDMap map = (OSDMap)OSDParser.DeserializeJson(body);
                //Make sure that the person who is calling can access the web service
                if (map.ContainsKey("WebPassword") && (map["WebPassword"] == m_password))
                {
                    method = map["Method"].AsString();
                    if (method == "Login")
                    {
                        resp = Login(map,false);
                    }
                    else if (method == "AdminLogin")
                    {
                        resp = Login(map,true);
                    }
                    else if (APIMethods.ContainsKey(method))
                    {
                        object[] args = new object[1]{map};
                        resp = (OSDMap)APIMethods[method].Invoke(this, args);
                    }
                    else
                    {
                        MainConsole.Instance.TraceFormat("[WebUI] Unsupported method called ({0})", method);
                    }
                }
                else
                {
                    MainConsole.Instance.Debug("Password does not match");
                }
            }
            catch (Exception e)
            {
                MainConsole.Instance.TraceFormat("[WebUI] Exception thrown: " + e.ToString());
            }
            if(resp.Count == 0){
                resp.Add("response", OSD.FromString("Failed"));
            }
            UTF8Encoding encoding = new UTF8Encoding();
            httpResponse.ContentType = "application/json";
            return encoding.GetBytes(OSDParser.SerializeJsonString(resp, true));
        }

        #endregion

        #region WebUI API methods

        #region Grid

        private OSDMap OnlineStatus(OSDMap map)
        {
            ILoginService loginService = m_registry.RequestModuleInterface<ILoginService>();
            bool LoginEnabled = loginService.MinLoginLevel == 0;

            OSDMap resp = new OSDMap();
            resp["Online"] = OSD.FromBoolean(true);
            resp["LoginEnabled"] = OSD.FromBoolean(LoginEnabled);

            return resp;
        }

        private OSDMap get_grid_info(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["GridInfo"] = GridInfo;
            return resp;
        }

        #endregion

        #region Account

        #region Registration

        private OSDMap CheckIfUserExists(OSDMap map)
        {
            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            UserAccount user = accountService.GetUserAccount(null, map["Name"].AsString());

            bool Verified = user != null;
            OSDMap resp = new OSDMap();
            resp["Verified"] = OSD.FromBoolean(Verified);
            resp["UUID"] = OSD.FromUUID(Verified ? user.PrincipalID : UUID.Zero);
            return resp;
        }

        private OSDMap CreateAccount(OSDMap map)
        {
            bool Verified = false;
            string Name = map["Name"].AsString();
            
            string Password = "";
            
            if (map.ContainsKey("Password"))
            {
            	Password = map["Password"].AsString();
            }
            else
            {
            	Password = map["PasswordHash"].AsString(); //is really plaintext password, the system hashes it later. Not sure why it was called PasswordHash to start with. I guess the original design was to have the PHP code generate the salt and password hash, then just simply store it here
            }
            
            //string PasswordSalt = map["PasswordSalt"].AsString(); //not being used
            string HomeRegion = map["HomeRegion"].AsString();
            string Email = map["Email"].AsString();
            string AvatarArchive = map["AvatarArchive"].AsString();
            int userLevel = map["UserLevel"].AsInteger();
            string UserTitle = map["UserTitle"].AsString();
            
            //server expects: 0 is PG, 1 is Mature, 2 is Adult - use this when setting MaxMaturity and MaturityRating
            //viewer expects: 13 is PG, 21 is Mature, 42 is Adult
            
            int MaxMaturity = 2; //set to adult by default
            if (map.ContainsKey("MaxMaturity")) //MaxMaturity is the highest level that they can change the maturity rating to in the viewer
            {
            	MaxMaturity = map["MaxMaturity"].AsInteger();
            }
            
            int MaturityRating = MaxMaturity; //set the default to whatever MaxMaturity was set tom incase they didn't define MaturityRating
            
            if (map.ContainsKey("MaturityRating")) //MaturityRating is the rating the user wants to be able to see
            {
            	MaturityRating = map["MaturityRating"].AsInteger();
            }
            
            bool activationRequired = map.ContainsKey("ActivationRequired") ? map["ActivationRequired"].AsBoolean() : false;

            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            if (accountService == null)
                return null;

            if (!Password.StartsWith("$1$"))
                Password = "$1$" + Util.Md5Hash(Password);
            Password = Password.Remove(0, 3); //remove $1$

            accountService.CreateUser(Name, Password, Email);
            UserAccount user = accountService.GetUserAccount(null, Name);
            IAgentInfoService agentInfoService = m_registry.RequestModuleInterface<IAgentInfoService> ();
            IGridService gridService = m_registry.RequestModuleInterface<IGridService> ();
            if (agentInfoService != null && gridService != null)
            {
                GridRegion r = gridService.GetRegionByName(null, HomeRegion);
                if (r != null)
                {
                    agentInfoService.SetHomePosition(user.PrincipalID.ToString(), r.RegionID, new Vector3(r.RegionSizeX / 2, r.RegionSizeY / 2, 20), Vector3.Zero);
                }
                else
                {
                    MainConsole.Instance.DebugFormat("[WebUI]: Could not set home position for user {0}, region \"{1}\" did not produce a result from the grid service", user.PrincipalID.ToString(), HomeRegion);
                }
            }

            Verified = user != null;
            UUID userID = UUID.Zero;

            OSDMap resp = new OSDMap();
            resp["Verified"] = OSD.FromBoolean(Verified);

            if (Verified)
            {
                userID = user.PrincipalID;
                user.UserLevel = userLevel;

                // could not find a way to save this data here.
                DateTime RLDOB = map["RLDOB"].AsDate();
				string RLGender = map["RLGender"].AsString();
                string RLName = map["RLName"].AsString();
                string RLAddress = map["RLAddress"].AsString();
                string RLCity = map["RLCity"].AsString();
                string RLZip = map["RLZip"].AsString();
                string RLCountry = map["RLCountry"].AsString();
                string RLIP = map["RLIP"].AsString();



                IAgentConnector con = DataPlugins.RequestPlugin<IAgentConnector>();
                con.CreateNewAgent (userID);

                IAgentInfo agent = con.GetAgent (userID);
                
                agent.MaxMaturity = MaxMaturity;
                agent.MaturityRating = MaturityRating;
                         
                agent.OtherAgentInformation["RLDOB"] = RLDOB;
				agent.OtherAgentInformation["RLGender"] = RLGender;
                agent.OtherAgentInformation["RLName"] = RLName;
                agent.OtherAgentInformation["RLAddress"] = RLAddress;
                agent.OtherAgentInformation["RLCity"] = RLCity;
                agent.OtherAgentInformation["RLZip"] = RLZip;
                agent.OtherAgentInformation["RLCountry"] = RLCountry;
                agent.OtherAgentInformation["RLIP"] = RLIP;
                if (activationRequired)
                {
                    UUID activationToken = UUID.Random();
                    agent.OtherAgentInformation["WebUIActivationToken"] = Util.Md5Hash(activationToken.ToString() + ":" + Password);
                    resp["WebUIActivationToken"] = activationToken;
                }
                con.UpdateAgent (agent);
                
                accountService.StoreUserAccount(user);

                IProfileConnector profileData = DataPlugins.RequestPlugin<IProfileConnector>();
                IUserProfileInfo profile = profileData.GetUserProfile(user.PrincipalID);
                if (profile == null)
                {
                    profileData.CreateNewProfile(user.PrincipalID);
                    profile = profileData.GetUserProfile(user.PrincipalID);
                }
                if (AvatarArchive.Length > 0)
                    profile.AArchiveName = AvatarArchive;

                profile.IsNewUser = true;
                
                profile.MembershipGroup = UserTitle;
                profile.CustomType = UserTitle;
                
                profileData.UpdateUserProfile(profile);
            }

            resp["UUID"] = OSD.FromUUID(userID);
            return resp;
        }

        private OSDMap GetAvatarArchives(OSDMap map)
        {
            OSDMap resp = new OSDMap();

            List<AvatarArchive> temp = m_registry.RequestModuleInterface<IAvatarAppearanceArchiver>().GetAvatarArchives();

            OSDArray names = new OSDArray();
            OSDArray snapshot = new OSDArray();

            MainConsole.Instance.DebugFormat("[WebUI] {0} avatar archives found", temp.Count);

            foreach (AvatarArchive a in temp)
            {
                names.Add(OSD.FromString(a.FileName));
                snapshot.Add(OSD.FromUUID(a.Snapshot));
            }

            resp["names"] = names;
            resp["snapshot"] = snapshot;

            return resp;
        }

        private OSDMap Authenticated(OSDMap map)
        {
            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            UserAccount user = accountService.GetUserAccount(null, map["UUID"].AsUUID());

            bool Verified = user != null;
            OSDMap resp = new OSDMap();
            resp["Verified"] = OSD.FromBoolean(Verified);

            if (Verified)
            {
                user.UserLevel = 0;
                accountService.StoreUserAccount(user);
                IAgentConnector con = DataPlugins.RequestPlugin<IAgentConnector>();
                IAgentInfo agent = con.GetAgent(user.PrincipalID);
                if (agent != null && agent.OtherAgentInformation.ContainsKey("WebUIActivationToken"))
                {
                    agent.OtherAgentInformation.Remove("WebUIActivationToken");
                    con.UpdateAgent(agent);
                }
            }

            return resp;
        }

        private OSDMap ActivateAccount(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Verified"] = OSD.FromBoolean(false);

            if (map.ContainsKey("UserName") && map.ContainsKey("PasswordHash") && map.ContainsKey("ActivationToken"))
            {
                IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
                UserAccount user = accountService.GetUserAccount(null, map["UserName"].ToString());
                if (user != null)
                {
                    IAgentConnector con = DataPlugins.RequestPlugin<IAgentConnector>();
                    IAgentInfo agent = con.GetAgent(user.PrincipalID);
                    if (agent != null && agent.OtherAgentInformation.ContainsKey("WebUIActivationToken"))
                    {
                        UUID activationToken = map["ActivationToken"];
                        string WebUIActivationToken = agent.OtherAgentInformation["WebUIActivationToken"];
                        string PasswordHash = map["PasswordHash"];
                        if (!PasswordHash.StartsWith("$1$"))
                        {
                            PasswordHash = "$1$" + Util.Md5Hash(PasswordHash);
                        }
                        PasswordHash = PasswordHash.Remove(0, 3); //remove $1$

                        bool verified = Utils.MD5String(activationToken.ToString() + ":" + PasswordHash) == WebUIActivationToken;
                        resp["Verified"] = verified;
                        if (verified)
                        {
                            user.UserLevel = 0;
                            accountService.StoreUserAccount(user);
                            agent.OtherAgentInformation.Remove("WebUIActivationToken");
                            con.UpdateAgent(agent);
                        }
                    }
                }
            }

            return resp;
        }

        #endregion

        #region Login

        private OSDMap Login(OSDMap map, bool asAdmin)
        {
            bool Verified = false;
            string Name = map["Name"].AsString();
            string Password = map["Password"].AsString();

            ILoginService loginService = m_registry.RequestModuleInterface<ILoginService>();
            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            UserAccount account = null;
            OSDMap resp = new OSDMap ();
            resp["Verified"] = OSD.FromBoolean(false);

            if (accountService == null || CheckIfUserExists(map)["Verified"] != true)
            {
                return resp;
            }

            account = m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(null, Name);

            //Null means it went through without an errorz
            if (loginService.VerifyClient(account.PrincipalID, Name, "UserAccount", Password))
            {
                if (asAdmin)
                {
                    IAgentInfo agent = DataPlugins.RequestPlugin<IAgentConnector>().GetAgent(account.PrincipalID);
                    if (agent.OtherAgentInformation["WebUIEnabled"].AsBoolean() == false)
                    {
                        return resp;
                    }
                }
                resp["UUID"] = OSD.FromUUID (account.PrincipalID);
                resp["FirstName"] = OSD.FromString (account.FirstName);
                resp["LastName"] = OSD.FromString (account.LastName);
                resp["Email"] = OSD.FromString(account.Email);
                Verified = true;
            }

            resp["Verified"] = OSD.FromBoolean (Verified);

            return resp;
        }

		private OSDMap Login2(OSDMap map)
		{
			string Name = map["Name"].AsString();
			string Password = map["Password"].AsString();

			OSDMap resp = new OSDMap ();
			resp["GoodLogin"] = OSD.FromBoolean(false);
			
			ILoginService loginService = m_registry.RequestModuleInterface<ILoginService>();
			IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
			UserAccount account = null;

			if (accountService == null || CheckIfUserExists(map)["Verified"] != true)
			{
				resp["why"] = OSD.FromString("AccountNotFound");
				return resp;
			}

			account = m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(null, Name);

			//Null means it went through without an error

			if (loginService.VerifyClient (account.PrincipalID, Name, "UserAccount", Password))
			{
				UUID agentID = account.PrincipalID;

				IAgentInfo agentInfo = DataPlugins.RequestPlugin<IAgentConnector>().GetAgent(agentID);

				bool banned = ((agentInfo.Flags & IAgentFlags.TempBan) == IAgentFlags.TempBan) || ((agentInfo.Flags & IAgentFlags.PermBan) == IAgentFlags.PermBan);

				if (banned) //get ban type
				{

					if ((agentInfo.Flags & IAgentFlags.PermBan) == IAgentFlags.PermBan)
					{
						resp["why"] = OSD.FromString("PermBan");
					}
					else if ((agentInfo.Flags & IAgentFlags.TempBan) == IAgentFlags.TempBan)
					{
						resp["why"] = OSD.FromString("TempBan");

						if (agentInfo.OtherAgentInformation.ContainsKey("TemperaryBanInfo") == true)
						{
							resp["BannedUntil"] = OSD.FromInteger(Util.ToUnixTime(agentInfo.OtherAgentInformation["TemperaryBanInfo"]));
						}
						else
						{
							resp["BannedUntil"] = OSD.FromInteger(0);
						}
					}
					else
					{
						resp["why"] = OSD.FromString("UnknownBan");
					}

					return resp;
				}
				else
				{
					resp["GoodLogin"] = OSD.FromBoolean(true);

					resp["UserLevel"] = OSD.FromInteger(account.UserLevel);
					resp["UUID"] = OSD.FromUUID (agentID);
					resp["FirstName"] = OSD.FromString (account.FirstName);
					resp["LastName"] = OSD.FromString (account.LastName);
					resp["Email"] = OSD.FromString(account.Email);

					return resp;
				}
			}
			else
			{
				resp["why"] = OSD.FromString("BadPassword");
				return resp;
			}
		}

        private OSDMap SetWebLoginKey(OSDMap map)
        {
            OSDMap resp = new OSDMap ();
            UUID principalID = map["PrincipalID"].AsUUID();
            UUID webLoginKey = UUID.Random();
            IAuthenticationService authService = m_registry.RequestModuleInterface<IAuthenticationService> ();
            if (authService != null)
            {
                //Remove the old
                DataPlugins.RequestPlugin<IAuthenticationData>().Delete(principalID, "WebLoginKey");
                authService.SetPlainPassword(principalID, "WebLoginKey", webLoginKey.ToString());
                resp["WebLoginKey"] = webLoginKey;
            }
            resp["Failed"] = OSD.FromString(String.Format("No auth service, cannot set WebLoginKey for user {0}.", map["PrincipalID"].AsUUID().ToString()));

            return resp;
        }

		private OSDMap SetUserLevel(OSDMap map) //just sets a user level
		{
			OSDMap resp = new OSDMap();

			UUID agentID = map ["UserID"].AsUUID();
            int userLevel = map["UserLevel"].AsInteger();

            IUserAccountService userService = m_registry.RequestModuleInterface<IUserAccountService>();
            UserAccount account = userService.GetUserAccount(null, agentID);

            if (account != null) //found
            {
                account.UserLevel = userLevel;
                
                userService.StoreUserAccount(account);
                
                resp["UserFound"] = OSD.FromBoolean(true);
                resp["Updated"] = OSD.FromBoolean(true);
            }
            else //not found
            {
                resp["UserFound"] = OSD.FromBoolean(false);
                resp["Updated"] = OSD.FromBoolean(false);
            }
            
			return resp;
		}

        #endregion

        #region Email

        /// <summary>
        /// After conformation the email is saved
        /// </summary>
        /// <param name="map">UUID, Email</param>
        /// <returns>Verified</returns>
        private OSDMap SaveEmail(OSDMap map)
        {
            string email = map["Email"].AsString();

            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            UserAccount user = accountService.GetUserAccount(null, map["UUID"].AsUUID());
            OSDMap resp = new OSDMap();

            bool verified = user != null;
            resp["Verified"] = OSD.FromBoolean(verified);
            if (verified)
            {
                user.Email = email;
                user.UserLevel = 0;
                accountService.StoreUserAccount(user);
            }
            return resp;
        }

        private OSDMap ConfirmUserEmailName(OSDMap map)
        {
            string Name = map["Name"].AsString();
            string Email = map["Email"].AsString();

            OSDMap resp = new OSDMap();
            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            UserAccount user = accountService.GetUserAccount(null, Name);
            bool verified = user != null;
            resp["Verified"] = OSD.FromBoolean(verified);

            if (verified)
            {
                resp["UUID"] = OSD.FromUUID(user.PrincipalID);
                if (user.UserLevel >= 0)
                {
                    if (user.Email.ToLower() != Email.ToLower())
                    {
                        MainConsole.Instance.TraceFormat("User email for account \"{0}\" is \"{1}\" but \"{2}\" was specified.", Name, user.Email.ToString(), Email);
                        resp["Error"] = OSD.FromString("Email does not match the user name.");
                        resp["ErrorCode"] = OSD.FromInteger(3);
                    }
                }
                else
                {
                    resp["Error"] = OSD.FromString("This account is disabled.");
                    resp["ErrorCode"] = OSD.FromInteger(2);
                }
            }
            else
            {
                resp["Error"] = OSD.FromString("No such user.");
                resp["ErrorCode"] = OSD.FromInteger(1);
            }

            return resp;
        }

        #endregion

        #region password

        private OSDMap ChangePassword(OSDMap map)
        {
            string Password = map["Password"].AsString();
            string newPassword = map["NewPassword"].AsString();

            ILoginService loginService = m_registry.RequestModuleInterface<ILoginService>();
            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            UUID userID = map["UUID"].AsUUID();
            UserAccount account = m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(null, userID);
            IAuthenticationService auths = m_registry.RequestModuleInterface<IAuthenticationService>();
            OSDMap resp = new OSDMap();
            //Null means it went through without an error
            bool Verified = loginService.VerifyClient(account.PrincipalID, account.Name, "UserAccount", Password);
            resp["Verified"] = OSD.FromBoolean(Verified);

            if ((auths.Authenticate(userID, "UserAccount", Util.Md5Hash(Password), 100) != string.Empty) && (Verified))
            {
                auths.SetPassword (userID, "UserAccount", newPassword);
            }

            return resp;
        }

        private OSDMap ForgotPassword(OSDMap map)
        {
            UUID UUDI = map["UUID"].AsUUID();
            string Password = map["Password"].AsString();

            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            UserAccount user = accountService.GetUserAccount(null, UUDI);

            OSDMap resp = new OSDMap();
            bool verified = user != null;
            resp["Verified"] = OSD.FromBoolean(verified);
            resp["UserLevel"] = OSD.FromInteger(0);
            if (verified)
            {
                resp["UserLevel"] = OSD.FromInteger(user.UserLevel);
                if (user.UserLevel >= 0)
                {
                    IAuthenticationService auths = m_registry.RequestModuleInterface<IAuthenticationService>();
                    auths.SetPassword (user.PrincipalID, "UserAccount", Password);
                }
                else
                {
                    resp["Verified"] = OSD.FromBoolean(false);
                }
            }

            return resp;
        }

        #endregion

        /// <summary>
        /// Changes user name
        /// </summary>
        /// <param name="map">UUID, FirstName, LastName</param>
        /// <returns>Verified</returns>
        private OSDMap ChangeName(OSDMap map)
        {
            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            UserAccount user = accountService.GetUserAccount(null, map["UUID"].AsUUID());
            OSDMap resp = new OSDMap();

            bool verified = user != null;
            resp["Verified"] = OSD.FromBoolean(verified);
            if (verified)
            {
                user.Name = map["Name"].AsString();
                resp["Stored" ] = OSD.FromBoolean(accountService.StoreUserAccount(user));
            }

            return resp;
        }

        private OSDMap EditUser(OSDMap map)
        {
            bool editRLInfo = (map.ContainsKey("RLName") && map.ContainsKey("RLAddress") && map.ContainsKey("RLZip") && map.ContainsKey("RLCity") && map.ContainsKey("RLCountry"));
            OSDMap resp = new OSDMap();
            resp["agent"] = OSD.FromBoolean(!editRLInfo); // if we have no RLInfo, editing account is assumed to be successful.
            resp["account"] = OSD.FromBoolean(false);
            UUID principalID = map["UserID"].AsUUID();
            UserAccount account = m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(null, principalID);
            if(account != null)
            {
                account.Email = map["Email"];
                if (m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(null, map["Name"].AsString()) == null)
                {
                    account.Name = map["Name"];
                }

                if (editRLInfo)
                {
                    IAgentConnector agentConnector = DataPlugins.RequestPlugin<IAgentConnector>();
                    IAgentInfo agent = agentConnector.GetAgent(account.PrincipalID);
                    if (agent == null)
                    {
                        agentConnector.CreateNewAgent(account.PrincipalID);
                        agent = agentConnector.GetAgent(account.PrincipalID);
                    }
                    if (agent != null)
                    {
                        agent.OtherAgentInformation["RLName"] = map["RLName"];
                        agent.OtherAgentInformation["RLAddress"] = map["RLAddress"];
                        agent.OtherAgentInformation["RLZip"] = map["RLZip"];
                        agent.OtherAgentInformation["RLCity"] = map["RLCity"];
                        agent.OtherAgentInformation["RLCountry"] = map["RLCountry"];
                        agentConnector.UpdateAgent(agent);
                        resp["agent"] = OSD.FromBoolean(true);
                    }
                }
                resp["account"] = OSD.FromBoolean(m_registry.RequestModuleInterface<IUserAccountService>().StoreUserAccount(account));
            }
            return resp;
        }

        #endregion

        #region Users

        /// <summary>
        /// Gets user information for change user info page on site
        /// </summary>
        /// <param name="map">UUID</param>
        /// <returns>Verified, HomeName, HomeUUID, Online, Email, FirstName, LastName</returns>
        private OSDMap GetGridUserInfo(OSDMap map)
        {
            string uuid = String.Empty;
            uuid = map["UUID"].AsString();

            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            UserAccount user = accountService.GetUserAccount(null, map["UUID"].AsUUID());
            IAgentInfoService agentService = m_registry.RequestModuleInterface<IAgentInfoService>();

            UserInfo userinfo;
            OSDMap resp = new OSDMap();
            bool verified = user != null;
            resp["Verified"] = OSD.FromBoolean(verified);
            if (verified)
            {
                userinfo = agentService.GetUserInfo(uuid);
                IGridService gs = m_registry.RequestModuleInterface<IGridService>();
                GridRegion gr = null;
                if (userinfo != null)
                {
                    gr = gs.GetRegionByUUID(null, userinfo.HomeRegionID);
                }

                resp["UUID"] = OSD.FromUUID(user.PrincipalID);
                resp["HomeUUID"] = OSD.FromUUID((userinfo == null) ? UUID.Zero : userinfo.HomeRegionID);
                resp["HomeName"] = OSD.FromString((userinfo == null) ? "" : gr.RegionName);
                resp["Online"] = OSD.FromBoolean((userinfo == null) ? false : userinfo.IsOnline);
                resp["Email"] = OSD.FromString(user.Email);
                resp["Name"] = OSD.FromString(user.Name);
                resp["FirstName"] = OSD.FromString(user.FirstName);
                resp["LastName"] = OSD.FromString(user.LastName);
            }

            return resp;
        }

        private OSDMap GetProfile(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            string Name = map["Name"].AsString();
            UUID userID = map["UUID"].AsUUID();

            UserAccount account = Name != "" ? 
                m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(null, Name) :
                 m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(null, userID);
            if (account != null)
            {
                OSDMap accountMap = new OSDMap();

                accountMap["Created"] = account.Created;
                accountMap["Name"] = account.Name;
                accountMap["PrincipalID"] = account.PrincipalID;
                accountMap["Email"] = account.Email;

                TimeSpan diff = DateTime.Now - Util.ToDateTime(account.Created);
                int years = (int)diff.TotalDays / 356;
                int days = years > 0 ? (int)diff.TotalDays / years : (int)diff.TotalDays;
                accountMap["TimeSinceCreated"] = years + " years, " + days + " days"; // if we're sending account.Created do we really need to send this string ?

                IProfileConnector profileConnector = DataPlugins.RequestPlugin<IProfileConnector>();
                IUserProfileInfo profile = profileConnector.GetUserProfile(account.PrincipalID);
                if (profile != null)
                {
                    resp["profile"] = profile.ToOSD(false);//not trusted, use false

                    if (account.UserFlags == 0)
                        account.UserFlags = 2; //Set them to no info given

                    string flags = ((IUserProfileInfo.ProfileFlags)account.UserFlags).ToString();
                    IUserProfileInfo.ProfileFlags.NoPaymentInfoOnFile.ToString();

                    accountMap["AccountInfo"] = (profile.CustomType != "" ? profile.CustomType :
                        account.UserFlags == 0 ? "Resident" : "Admin") + "\n" + flags;
                    UserAccount partnerAccount = m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(null, profile.Partner);
                    if (partnerAccount != null)
                    {
                        accountMap["Partner"] = partnerAccount.Name;
                        accountMap["PartnerUUID"] = partnerAccount.PrincipalID;
                    }
                    else
                    {
                        accountMap["Partner"] = "";
                        accountMap["PartnerUUID"] = UUID.Zero;
                    }

                }
                IAgentConnector agentConnector = DataPlugins.RequestPlugin<IAgentConnector>();
                IAgentInfo agent = agentConnector.GetAgent(account.PrincipalID);
                if(agent != null)
                {
                    OSDMap agentMap = new OSDMap();
                    agentMap["RLName"] = agent.OtherAgentInformation["RLName"].AsString();
                    agentMap["RLGender"] = agent.OtherAgentInformation["RLGender"].AsString();
                    agentMap["RLAddress"] = agent.OtherAgentInformation["RLAddress"].AsString();
                    agentMap["RLZip"] = agent.OtherAgentInformation["RLZip"].AsString();
                    agentMap["RLCity"] = agent.OtherAgentInformation["RLCity"].AsString();
                    agentMap["RLCountry"] = agent.OtherAgentInformation["RLCountry"].AsString();
                    resp["agent"] = agentMap;
                }
                resp["account"] = accountMap;
            }

            return resp;
        }

        private OSDMap DeleteUser(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Finished"] = OSD.FromBoolean(true);

            UUID agentID = map["UserID"].AsUUID();
            IAgentInfo GetAgent = DataPlugins.RequestPlugin<IAgentConnector>().GetAgent(agentID);

            if (GetAgent != null)
            {
                GetAgent.Flags &= ~IAgentFlags.PermBan;
                DataPlugins.RequestPlugin<IAgentConnector>().UpdateAgent(GetAgent);
            }
            return resp;
        }

        #region messaging

        private OSDMap KickUser(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Finished"] = OSD.FromBoolean(true);

            UUID agentID = map["UserID"].AsUUID();
            
            IGridWideMessageModule messageModule = m_registry.RequestModuleInterface<IGridWideMessageModule>();
            if (messageModule != null)
                messageModule.KickUser(agentID, map["Message"].AsString());
            
            return resp;
        }
        
        private OSDMap GridWideAlert(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Finished"] = OSD.FromBoolean(true);
            
            IGridWideMessageModule messageModule = m_registry.RequestModuleInterface<IGridWideMessageModule>();
            if (messageModule != null)
                messageModule.SendAlert(map["Message"].AsString());
            
            return resp;
        }
        
        private OSDMap MessageUser(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Finished"] = OSD.FromBoolean(true);
            
            UUID agentID = map["UserID"].AsUUID();
            
            IGridWideMessageModule messageModule = m_registry.RequestModuleInterface<IGridWideMessageModule>();
            if (messageModule != null)
                messageModule.MessageUser(agentID, map["Message"].AsString());
            
            return resp;
        }

        #endregion


        #region banning

        private void doBan(UUID agentID, DateTime? until){
            var conn = DataPlugins.RequestPlugin<IAgentConnector>();
            IAgentInfo agentInfo = conn.GetAgent(agentID);
            if (agentInfo != null)
            {
                agentInfo.Flags |= (until.HasValue) ? IAgentFlags.TempBan : IAgentFlags.PermBan;

                if (until.HasValue)
                {
                    agentInfo.OtherAgentInformation["TemperaryBanInfo"] = until.Value;
                    MainConsole.Instance.TraceFormat("Temp ban for {0} until {1}", agentID, until.Value.ToString("s"));
                }
                conn.UpdateAgent(agentInfo);
            }
        }

        private OSDMap BanUser(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Finished"] = OSD.FromBoolean(true);
            UUID agentID = map["UserID"].AsUUID();
            doBan(agentID,null);

            return resp;
        }

        private OSDMap TempBanUser(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Finished"] = OSD.FromBoolean(true);
            UUID agentID = map["UserID"].AsUUID();
            DateTime until = Util.ToDateTime(map["BannedUntil"].AsInteger()); //BannedUntil is a unix timestamp of the date and time the user should be banned till
            doBan(agentID, until);

            return resp;
        }

        private OSDMap UnBanUser(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Finished"] = OSD.FromBoolean(true);

            UUID agentID = map["UserID"].AsUUID();
            IAgentInfo GetAgent = DataPlugins.RequestPlugin<IAgentConnector>().GetAgent(agentID);

            if (GetAgent != null)
            {
                GetAgent.Flags &= IAgentFlags.PermBan;
                GetAgent.Flags &= IAgentFlags.TempBan;
                if (GetAgent.OtherAgentInformation.ContainsKey("TemperaryBanInfo") == true)
                    GetAgent.OtherAgentInformation.Remove("TemperaryBanInfo");

                DataPlugins.RequestPlugin<IAgentConnector>().UpdateAgent(GetAgent);
            }

            return resp;
        }

        private OSDMap CheckBan(OSDMap map)
        {
            OSDMap resp = new OSDMap();

            UUID agentID = map["UserID"].AsUUID();
            IAgentInfo agentInfo = DataPlugins.RequestPlugin<IAgentConnector>().GetAgent(agentID);

            if (agentInfo != null) //found
            {
                resp["UserFound"] = OSD.FromBoolean(true);

                bool banned = ((agentInfo.Flags & IAgentFlags.TempBan) == IAgentFlags.TempBan) || ((agentInfo.Flags & IAgentFlags.PermBan) == IAgentFlags.PermBan);

                resp["banned"] = OSD.FromBoolean(banned);

                if (banned) //get ban type
                {
                    if ((agentInfo.Flags & IAgentFlags.PermBan) == IAgentFlags.PermBan)
                    {
                        resp["BanType"] = OSD.FromString("PermBan");
                    }
                    else if ((agentInfo.Flags & IAgentFlags.TempBan) == IAgentFlags.TempBan)
                    {
                        resp["BanType"] = OSD.FromString("TempBan");
                        if (agentInfo.OtherAgentInformation.ContainsKey("TemperaryBanInfo") == true)
                        {
                            resp["BannedUntil"] = OSD.FromInteger(Util.ToUnixTime(agentInfo.OtherAgentInformation["TemperaryBanInfo"]));
                        }
                        else
                        {
                            resp["BannedUntil"] = OSD.FromInteger(0);
                        }
                    }
                    else
                    {
                        resp["BanType"] = OSD.FromString("Unknown");
                    }
                }
            }
            else //not found
            {
                resp["UserFound"] = OSD.FromBoolean(false);
            }

            return resp;
        }

        #endregion

        private OSDMap FindUsers(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            int start = map["Start"].AsInteger();
            int end = map["End"].AsInteger();
            string Query = map["Query"].AsString();
            List<UserAccount> accounts = m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccounts(null, Query);

            OSDArray users = new OSDArray();
            MainConsole.Instance.TraceFormat("{0} accounts found", accounts.Count);
            for(int i = start; i < end && i < accounts.Count; i++)
            {
                UserAccount acc = accounts[i];
                OSDMap userInfo = new OSDMap();
                userInfo["PrincipalID"] = acc.PrincipalID;
                userInfo["UserName"] = acc.Name;
                userInfo["Created"] = acc.Created;
                userInfo["UserFlags"] = acc.UserFlags;
                users.Add(userInfo);
            }
            resp["Users"] = users;

            resp["Start"] = OSD.FromInteger(start);
            resp["End"] = OSD.FromInteger(end);
            resp["Query"] = OSD.FromString(Query);

            return resp;
        }

        private OSDMap GetFriends(OSDMap map)
        {
            OSDMap resp = new OSDMap();

            if (map.ContainsKey("UserID") == false)
            {
                resp["Failed"] = OSD.FromString("User ID not specified.");
                return resp;
            }

            IFriendsService friendService = m_registry.RequestModuleInterface<IFriendsService>();

            if (friendService == null)
            {
                resp["Failed"] = OSD.FromString("No friend service found.");
                return resp;
            }

            List<FriendInfo> friendsList = new List<FriendInfo>(friendService.GetFriends(map["UserID"].AsUUID()));
            OSDArray friends = new OSDArray(friendsList.Count);
            foreach (FriendInfo friendInfo in friendsList)
            {
                UserAccount account = m_registry.RequestModuleInterface<IUserAccountService>().GetUserAccount(null, UUID.Parse(friendInfo.Friend));
                OSDMap friend = new OSDMap(4);
                friend["PrincipalID"] = friendInfo.Friend;
                friend["Name"] = account.Name;
                friend["MyFlags"] = friendInfo.MyFlags;
                friend["TheirFlags"] = friendInfo.TheirFlags;
                friends.Add(friend);
            }

            resp["Friends"] = friends;

            return resp;
        }

        #endregion

        #region IAbuseReports

        private OSDMap GetAbuseReports(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            IAbuseReports ar = m_registry.RequestModuleInterface<IAbuseReports>();

            int start = map["Start"].AsInteger();
            int count = map["Count"].AsInteger();
            bool active = map["Active"].AsBoolean();

            List<AbuseReport> lar = ar.GetAbuseReports(start, count, active);
            OSDArray AbuseReports = new OSDArray();
            foreach (AbuseReport tar in lar)
            {
                AbuseReports.Add(tar.ToOSD());
            }

            resp["AbuseReports"] = AbuseReports;
            resp["Start"] = OSD.FromInteger(start);
            resp["Count"] = OSD.FromInteger(count); // we're not using the AbuseReports.Count because client implementations of the WebUI API can check the count themselves. This is just for showing the input.
            resp["Active"] = OSD.FromBoolean(active);

            return resp;
        }

        private OSDMap AbuseReportMarkComplete(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            IAbuseReports ar = m_registry.RequestModuleInterface<IAbuseReports>();
            AbuseReport tar = ar.GetAbuseReport(map["Number"].AsInteger(), map["WebPassword"].AsString());
            if (tar != null)
            {
                tar.Active = false;
                ar.UpdateAbuseReport(tar, map["WebPassword"].AsString());
                resp["Finished"] = OSD.FromBoolean(true);
            }
            else
            {
                resp["Finished"] = OSD.FromBoolean(false);
                resp["Failed"] = OSD.FromString(String.Format("No abuse report found with specified number {0}", map["Number"].AsInteger()));
            }

            return resp;
        }

        private OSDMap AbuseReportSaveNotes(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            IAbuseReports ar = m_registry.RequestModuleInterface<IAbuseReports>();
            AbuseReport tar = ar.GetAbuseReport(map["Number"].AsInteger(), map["WebPassword"].AsString());
            if (tar != null)
            {
                tar.Notes = map["Notes"].ToString();
                ar.UpdateAbuseReport(tar, map["WebPassword"].AsString());
                resp["Finished"] = OSD.FromBoolean(true);
            }
            else
            {
                resp["Finished"] = OSD.FromBoolean(false);
                resp["Failed"] = OSD.FromString(String.Format("No abuse report found with specified number {0}", map["Number"].AsInteger()));
            }

            return resp;
        }

        #endregion

        #region Places

        #region Regions

        private OSDMap GetRegions(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            RegionFlags type = map.Keys.Contains("RegionFlags") ? (RegionFlags)map["RegionFlags"].AsInteger() : RegionFlags.RegionOnline;
            int start = map.Keys.Contains("Start") ? map["Start"].AsInteger() : 0;
            if(start < 0){
                start = 0;
            }
            int count = map.Keys.Contains("Count") ? map["Count"].AsInteger() : 10;
            if(count < 0){
                count = 1;
            }

            IRegionData regiondata = DataPlugins.RequestPlugin<IRegionData>();

            Dictionary<string, bool> sort = new Dictionary<string, bool>();

            string[] supportedSort = new string[3]{
                "SortRegionName",
                "SortLocX",
                "SortLocY"
            };

            foreach (string sortable in supportedSort)
            {
                if (map.ContainsKey(sortable))
                {
                    sort[sortable.Substring(4)] = map[sortable].AsBoolean();
                }
            }

            List<GridRegion> regions = regiondata.Get(type, sort);
            OSDArray Regions = new OSDArray();
            if (start < regions.Count)
            {
                int i = 0;
                int j = regions.Count <= (start + count) ? regions.Count : (start + count);
                for (i = start; i < j; ++i)
                {
                    Regions.Add(regions[i].ToOSD());
                }
            }
            resp["Start"] = OSD.FromInteger(start);
            resp["Count"] = OSD.FromInteger(count);
            resp["Total"] = OSD.FromInteger(regions.Count);
            resp["Regions"] = Regions;
            return resp;
        }

        private OSDMap GetRegion(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            IRegionData regiondata = DataPlugins.RequestPlugin<IRegionData>();
            if (regiondata != null && (map.ContainsKey("RegionID") || map.ContainsKey("Region")))
            {
                string regionName = map.ContainsKey("Region") ? map["Region"].ToString().Trim() : "";
                UUID regionID = map.ContainsKey("RegionID") ? UUID.Parse(map["RegionID"].ToString()) : UUID.Zero;
                UUID scopeID = map.ContainsKey("ScopeID") ? UUID.Parse(map["ScopeID"].ToString()) : UUID.Zero;
                GridRegion region=null;
                if (regionID != UUID.Zero)
                {
                    region = regiondata.Get(regionID, null);
                }else if(regionName != string.Empty){
                    region = regiondata.Get(regionName, null, null, null)[0];
                }
                if (region != null)
                {
                    resp["Region"] = region.ToOSD();
                }
            }
            return resp;
        }

        #endregion

        #region Parcels

        private static OSDMap LandData2WebOSD(LandData parcel){
            OSDMap parcelOSD = parcel.ToOSD();
            parcelOSD["GenericData"] = parcelOSD.ContainsKey("GenericData") ? (parcelOSD["GenericData"].Type == OSDType.Map ? parcelOSD["GenericData"] : (OSDMap)OSDParser.DeserializeLLSDXml(parcelOSD["GenericData"].ToString())) : new OSDMap();
            parcelOSD["Bitmap"] = OSD.FromBinary(parcelOSD["Bitmap"]).ToString();
            return parcelOSD;
        }

        private OSDMap GetParcelsByRegion(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Parcels"] = new OSDArray();
            resp["Total"] = OSD.FromInteger(0);

            IDirectoryServiceConnector directory = DataPlugins.RequestPlugin<IDirectoryServiceConnector>();

            if (directory != null && map.ContainsKey("Region") == true)
            {
                UUID RegionID = UUID.Parse(map["Region"]);
                UUID ScopeID = map.ContainsKey("ScopeID") ? UUID.Parse(map["ScopeID"].ToString()) : UUID.Zero;
                UUID owner = map.ContainsKey("Owner") ? UUID.Parse(map["Owner"].ToString()) : UUID.Zero;
                uint start = map.ContainsKey("Start") ? uint.Parse(map["Start"].ToString()) : 0;
                uint count = map.ContainsKey("Count") ? uint.Parse(map["Count"].ToString()) : 10;
                ParcelFlags flags = map.ContainsKey("Flags") ? (ParcelFlags)int.Parse(map["Flags"].ToString()) : ParcelFlags.None;
                ParcelCategory category = map.ContainsKey("Category") ? (ParcelCategory)uint.Parse(map["Flags"].ToString()) : ParcelCategory.Any;
                uint total = directory.GetNumberOfParcelsByRegion(RegionID, owner, flags, category);
                if (total > 0)
                {
                    resp["Total"] = OSD.FromInteger((int)total);
                    if(count == 0){
                        return resp;
                    }
                    List<LandData> parcels = directory.GetParcelsByRegion(start, count, RegionID, owner, flags, category);
                    OSDArray Parcels = new OSDArray(parcels.Count);
                    parcels.ForEach(delegate(LandData parcel)
                    {
                        Parcels.Add(LandData2WebOSD(parcel));
                    });
                    resp["Parcels"] = Parcels;
                }
            }

            return resp;
        }

        private OSDMap GetParcel(OSDMap map)
        {
            OSDMap resp = new OSDMap();

            UUID regionID = map.ContainsKey("RegionID") ? UUID.Parse(map["RegionID"].ToString()) : UUID.Zero;
            UUID scopeID = map.ContainsKey("ScopeID") ? UUID.Parse(map["ScopeID"].ToString()) : UUID.Zero;
            UUID parcelID = map.ContainsKey("ParcelInfoUUID") ? UUID.Parse(map["ParcelInfoUUID"].ToString()) : UUID.Zero;
            string parcelName = map.ContainsKey("Parcel") ? map["Parcel"].ToString().Trim() : string.Empty;

            IDirectoryServiceConnector directory = DataPlugins.RequestPlugin<IDirectoryServiceConnector>();

            if (directory != null && (parcelID != UUID.Zero || (regionID != UUID.Zero && parcelName != string.Empty)))
            {
                LandData parcel = null;

                if(parcelID != UUID.Zero){
                    parcel = directory.GetParcelInfo(parcelID);
                }else if(regionID != UUID.Zero && parcelName != string.Empty){
                    parcel = directory.GetParcelInfo(regionID, parcelName);
                }

                if (parcel != null)
                {
                    resp["Parcel"] = LandData2WebOSD(parcel);
                }
            }

            return resp;
        }

        #endregion

        #endregion

        #region GroupRecord

        private static OSDMap GroupRecord2OSDMap(GroupRecord group)
        {
            OSDMap resp = new OSDMap();
            resp["GroupID"] = group.GroupID;
            resp["GroupName"] = group.GroupName;
            resp["AllowPublish"] = group.AllowPublish;
            resp["MaturePublish"] = group.MaturePublish;
            resp["Charter"] = group.Charter;
            resp["FounderID"] = group.FounderID;
            resp["GroupPicture"] = group.GroupPicture;
            resp["MembershipFee"] = group.MembershipFee;
            resp["OpenEnrollment"] = group.OpenEnrollment;
            resp["OwnerRoleID"] = group.OwnerRoleID;
            resp["ShowInList"] = group.ShowInList;
            return resp;
        }

        private OSDMap GetGroups(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            uint start = map.ContainsKey("Start") ? map["Start"].AsUInteger() : 0;
            resp["Start"] = start;
            resp["Total"] = 0;

            IGroupsServiceConnector groups = DataPlugins.RequestPlugin<IGroupsServiceConnector>();
            OSDArray Groups = new OSDArray();
            if (groups != null)
            {
                Dictionary<string, bool> sort       = new Dictionary<string, bool>();
                Dictionary<string, bool> boolFields = new Dictionary<string, bool>();

                if (map.ContainsKey("Sort") && map["Sort"].Type == OSDType.Map)
                {
                    OSDMap fields = (OSDMap)map["Sort"];
                    foreach (string field in fields.Keys)
                    {
                        sort[field] = int.Parse(fields[field]) != 0;
                    }
                }
                if (map.ContainsKey("BoolFields") && map["BoolFields"].Type == OSDType.Map)
                {
                    OSDMap fields = (OSDMap)map["BoolFields"];
                    foreach (string field in fields.Keys)
                    {
                        boolFields[field] = int.Parse(fields[field]) != 0;
                    }
                }
                List<GroupRecord> reply = groups.GetGroupRecords(
                    AdminAgentID,
                    start,
                    map.ContainsKey("Count") ? map["Count"].AsUInteger() : 10,
                    sort,
                    boolFields
                );
                if (reply.Count > 0)
                {
                    foreach (GroupRecord groupReply in reply)
                    {
                        Groups.Add(GroupRecord2OSDMap(groupReply));
                    }
                }
                resp["Total"] = groups.GetNumberOfGroups(AdminAgentID, boolFields);
            }

            resp["Groups"] = Groups;
            return resp;
        }

        private OSDMap GetGroup(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            IGroupsServiceConnector groups = DataPlugins.RequestPlugin<IGroupsServiceConnector>();
            resp["Group"] = false;
            if (groups != null && (map.ContainsKey("Name") || map.ContainsKey("UUID")))
            {
                UUID groupID = map.ContainsKey("UUID") ? UUID.Parse(map["UUID"].ToString()) : UUID.Zero;
                string name = map.ContainsKey("Name") ? map["Name"].ToString() : "";
                GroupRecord reply = groups.GetGroupRecord(AdminAgentID, groupID, name);
                if (reply != null)
                {
                    resp["Group"] = GroupRecord2OSDMap(reply);
                }
            }
            return resp;
        }

        #region GroupNoticeData

        private OSDMap GroupAsNewsSource(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["Verified"] = OSD.FromBoolean(false);
            IGenericsConnector generics = DataPlugins.RequestPlugin<IGenericsConnector>();
            UUID groupID;
            if (generics != null && map.ContainsKey("Group") == true && map.ContainsKey("Use") && UUID.TryParse(map["Group"], out groupID) == true)
            {
                if (map["Use"].AsBoolean())
                {
                    OSDMap useValue = new OSDMap();
                    useValue["Use"] = OSD.FromBoolean(true);
                    generics.AddGeneric(groupID, "Group", "WebUI_newsSource", useValue);
                }
                else
                {
                    generics.RemoveGeneric(groupID, "Group", "WebUI_newsSource");
                }
                resp["Verified"] = OSD.FromBoolean(true);
            }
            return resp;
        }

        private OSDMap GroupNotices(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["GroupNotices"] = new OSDArray();
            resp["Total"] = 0;
            IGroupsServiceConnector groups = DataPlugins.RequestPlugin<IGroupsServiceConnector>();

            if (map.ContainsKey("Groups") && groups != null && map["Groups"].Type.ToString() == "Array")
            {
                OSDArray groupIDs = (OSDArray)map["Groups"];
                List<UUID> GroupIDs = new List<UUID>();
                foreach (string groupID in groupIDs)
                {
                    UUID foo;
                    if (UUID.TryParse(groupID, out foo))
                    {
                        GroupIDs.Add(foo);
                    }
                }
                if (GroupIDs.Count > 0)
                {
                    uint start = map.ContainsKey("Start") ? uint.Parse(map["Start"]) : 0;
                    uint count = map.ContainsKey("Count") ? uint.Parse(map["Count"]) : 10;
                    List<GroupNoticeData> groupNotices = groups.GetGroupNotices(AdminAgentID, start, count, GroupIDs);
                    OSDArray GroupNotices = new OSDArray(groupNotices.Count);
                    foreach (GroupNoticeData GND in groupNotices)
                    {
                        OSDMap gnd = new OSDMap();
                        gnd["GroupID"] = OSD.FromUUID(GND.GroupID);
                        gnd["NoticeID"] = OSD.FromUUID(GND.NoticeID);
                        gnd["Timestamp"] = OSD.FromInteger((int)GND.Timestamp);
                        gnd["FromName"] = OSD.FromString(GND.FromName);
                        gnd["Subject"] = OSD.FromString(GND.Subject);
                        gnd["HasAttachment"] = OSD.FromBoolean(GND.HasAttachment);
                        gnd["ItemID"] = OSD.FromUUID(GND.ItemID);
                        gnd["AssetType"] = OSD.FromInteger((int)GND.AssetType);
                        gnd["ItemName"] = OSD.FromString(GND.ItemName);
                        GroupNoticeInfo notice = groups.GetGroupNotice(AdminAgentID, GND.NoticeID);
                        gnd["Message"] = OSD.FromString(groups.GetGroupNotice(AdminAgentID, GND.NoticeID).Message);
                        GroupNotices.Add(gnd);
                    }
                    resp["GroupNotices"] = GroupNotices;
                    resp["Total"] = (int)groups.GetNumberOfGroupNotices(AdminAgentID, GroupIDs);
                }
            }

            return resp;
        }

        private OSDMap NewsFromGroupNotices(OSDMap map)
        {
            OSDMap resp = new OSDMap();
            resp["GroupNotices"] = new OSDArray();
            resp["Total"] = 0;
            IGenericsConnector generics = DataPlugins.RequestPlugin<IGenericsConnector>();
            IGroupsServiceConnector groups = DataPlugins.RequestPlugin<IGroupsServiceConnector>();
            if (generics == null || groups == null)
            {
                return resp;
            }
            OSDMap useValue = new OSDMap();
            useValue["Use"] = OSD.FromBoolean(true);
            List<UUID> GroupIDs = generics.GetOwnersByGeneric("Group", "WebUI_newsSource", useValue);
            if (GroupIDs.Count <= 0)
            {
                return resp;
            }
            foreach (UUID groupID in GroupIDs)
            {
                GroupRecord group = groups.GetGroupRecord(AdminAgentID, groupID, "");
                if (!group.ShowInList)
                {
                    GroupIDs.Remove(groupID);
                }
            }

            uint start = map.ContainsKey("Start") ? uint.Parse(map["Start"].ToString()) : 0;
            uint count = map.ContainsKey("Count") ? uint.Parse(map["Count"].ToString()) : 10;

            OSDMap args = new OSDMap();
            args["Start"] = OSD.FromString(start.ToString());
            args["Count"] = OSD.FromString(count.ToString());
            args["Groups"] = new OSDArray(GroupIDs.ConvertAll(x=>OSD.FromString(x.ToString())));

            return GroupNotices(args);
        }

        #endregion

        #endregion

        #endregion
    }
}