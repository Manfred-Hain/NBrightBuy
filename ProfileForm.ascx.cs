﻿// --- Copyright (c) notice NevoWeb ---
//  Copyright (c) 2014 SARL NevoWeb.  www.nevoweb.com. The MIT License (MIT).
// Author: D.C.Lee
// ------------------------------------------------------------------------
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
// ------------------------------------------------------------------------
// This copyright notice may NOT be removed, obscured or modified without written consent from the author.
// --- End copyright notice --- 

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.UI.WebControls;
using DotNetNuke.Common;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Portals;
using DotNetNuke.Entities.Users;
using DotNetNuke.Security;
using DotNetNuke.Security.Membership;
using DotNetNuke.Services.Authentication;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.Localization;
using DotNetNuke.UI.UserControls;
using NBrightCore.common;
using NBrightCore.render;
using NBrightDNN;

using Nevoweb.DNN.NBrightBuy.Base;
using Nevoweb.DNN.NBrightBuy.Components;
using DataProvider = DotNetNuke.Data.DataProvider;

namespace Nevoweb.DNN.NBrightBuy
{

    /// -----------------------------------------------------------------------------
    /// <summary>
    /// The ViewNBrightGen class displays the content
    /// </summary>
    /// -----------------------------------------------------------------------------
    public partial class ProfileForm : NBrightBuyFrontOfficeBase
    {

        private String _templinp = "";
        private ProfileData _profileData;
        private const string NotifyRef = "profileupdated";

        #region Event Handlers


        override protected void OnInit(EventArgs e)
        {
            base.OnInit(e);

            _profileData = new ProfileData();

            if (ModSettings.Get("themefolder") == "")  // if we don't have module setting jump out
            {
                rpInp.ItemTemplate = new GenXmlTemplate("NO MODULE SETTINGS");
                return;
            }

            try
            {
                _templinp = ModSettings.Get("txtinputform");

                // Get Display
                var rpInpTempl = ModCtrl.GetTemplateData(ModSettings, _templinp, Utils.GetCurrentCulture(), DebugMode);
                rpInp.ItemTemplate = NBrightBuyUtils.GetGenXmlTemplate(rpInpTempl, ModSettings.Settings(), PortalSettings.HomeDirectory); 
            }
            catch (Exception exc)
            {
                rpInp.ItemTemplate = new GenXmlTemplate(exc.Message, ModSettings.Settings());
                // catch any error and allow processing to continue, output error as footer template.
            }

        }

        protected override void OnLoad(EventArgs e)
        {
            try
            {
                base.OnLoad(e);
                if (Page.IsPostBack == false)
                {
                    PageLoad();
                }
            }
            catch (Exception exc) //Module failed to load
            {
                //display the error on the template (don;t want to log it here, prefer to deal with errors directly.)
                var l = new Literal();
                l.Text = exc.ToString();
                phData.Controls.Add(l);
            }
        }

        private void PageLoad()
        {

            var objprof = _profileData.GetProfile();
            if (!_profileData.Exists || objprof == null)
            {
                objprof = new NBrightInfo(true); //assume new address
                objprof.XMLData = NBrightBuyUtils.GetFormTempData(ModuleId); // get any saved data
            }
            if (UserId >= 0)
            {
                var cData = new ClientData(PortalSettings.PortalId, UserId);
                objprof.AddXmlNode("<client>" + cData.GetInfo().XMLData + "</client>", "client", "genxml");
                if (StoreSettings.Current.DebugMode) objprof.XMLDoc.Save(PortalSettings.Current.HomeDirectoryMapPath + "debug_profile.xml");
            }
            base.DoDetail(rpInp, objprof);

        }

        #endregion


        #region  "Events "

        protected void CtrlItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var cArg = e.CommandArgument.ToString();
            var param = new string[3];
            var redirecttabid = "";
            var emailtemplate = "";

            switch (e.CommandName.ToLower())
            {
                case "saveprofile":
                    _profileData.UpdateProfile(rpInp, DebugMode);

                    emailtemplate = ModSettings.Get("emailtemplate");
                    if (emailtemplate != "") NBrightBuyUtils.SendEmailToManager(emailtemplate, _profileData.GetProfile(), "profileupdated_emailsubject.Text");

                    param[0] = "msg=" + NotifyRef + "_" + NotifyCode.ok;
                    NBrightBuyUtils.SetNotfiyMessage(ModuleId, NotifyRef, NotifyCode.ok);
                    Response.Redirect(Globals.NavigateURL(TabId, "", param), true);
                    break;
                case "register":

                    var notifyCode = NotifyCode.fail;
                    var failreason = "";

                    var cap = (DotNetNuke.UI.WebControls.CaptchaControl)rpInp.Controls[0].FindControl("captcha");;
                    if (cap == null || cap.IsValid)
                    {
                        //create a new user and login
                        if (!this.UserInfo.IsInRole("Registered Users"))
                        {
                            // Create and hydrate User
                            var objUser = new UserInfo();
                            objUser.Profile.InitialiseProfile(this.PortalId, true);
                            objUser.PortalID = PortalId;
                            objUser.DisplayName = GenXmlFunctions.GetField(rpInp, "DisplayName");
                            objUser.Email = GenXmlFunctions.GetField(rpInp, "Email");
                            objUser.FirstName = GenXmlFunctions.GetField(rpInp, "FirstName");
                            objUser.LastName = GenXmlFunctions.GetField(rpInp, "LastName");
                            objUser.Username = GenXmlFunctions.GetField(rpInp, "Username");
                            objUser.Profile.PreferredLocale = Utils.GetCurrentCulture();

                            if (objUser.Username == "") objUser.Username = GenXmlFunctions.GetField(rpInp, "Email");
                            objUser.Membership.CreatedDate = System.DateTime.Now;
                            var passwd = GenXmlFunctions.GetField(rpInp, "Password");
                            if (passwd == "")
                            {
                                objUser.Membership.UpdatePassword = true;
                                passwd = UserController.GeneratePassword(9);
                            }
                            objUser.Membership.Password = passwd; 
                            objUser.Membership.Approved = PortalSettings.UserRegistration == (int) Globals.PortalRegistrationType.PublicRegistration;

                            // Create the user
                            var createStatus = UserController.CreateUser(ref objUser);

                            DataCache.ClearPortalCache(PortalId, true);

                            switch (createStatus)
                            {
                                case UserCreateStatus.Success:
                                    //boNotify = true;
                                    if (objUser.Membership.Approved) UserController.UserLogin(this.PortalId, objUser, PortalSettings.PortalName, AuthenticationLoginBase.GetIPAddress(), false);
                                    notifyCode = NotifyCode.ok;
                                    break;
                                case UserCreateStatus.DuplicateEmail:
                                    failreason = "exists";
                                    break;
                                case UserCreateStatus.DuplicateUserName:
                                    failreason = "exists";
                                    break;
                                case UserCreateStatus.UsernameAlreadyExists:
                                    failreason = "exists";
                                    break;
                                case UserCreateStatus.UserAlreadyRegistered:
                                    failreason = "exists";
                                    break;
                                default:
                                    // registration error
                                    break;
                            }

                            if (notifyCode == NotifyCode.ok)
                            {
                                _profileData = new ProfileData(objUser.UserID, rpInp, DebugMode); //create and update a profile for this new logged in user.
                                emailtemplate = ModSettings.Get("emailregisteredtemplate");
                                if (emailtemplate != "") NBrightBuyUtils.SendEmailToManager(emailtemplate, _profileData.GetProfile(), "profileregistered_emailsubject.Text");
                                emailtemplate = ModSettings.Get("emailregisteredclienttemplate");
                                if (emailtemplate != "") NBrightBuyUtils.SendEmail(objUser.Email, emailtemplate, _profileData.GetProfile(), "profileregistered_emailsubject.Text", "", objUser.Profile.PreferredLocale);
                            }

                        }

                    }
                    else
                    {
                        NBrightBuyUtils.SetFormTempData(ModuleId,GenXmlFunctions.GetGenXml(rpInp));
                        failreason = "captcha";
                    }

                    param[0] = "msg=" + NotifyRef + "_" + notifyCode;
                    if (!UserInfo.IsInRole(StoreSettings.ClientEditorRole) && ModSettings.Get("clientrole") == "True" && notifyCode == NotifyCode.ok)
                        NBrightBuyUtils.SetNotfiyMessage(ModuleId, NotifyRef + "clientrole", notifyCode);
                    else
                    {
                        NBrightBuyUtils.SetNotfiyMessage(ModuleId, NotifyRef + failreason, notifyCode);
                    }

                    if (notifyCode == NotifyCode.ok) redirecttabid = ModSettings.Get("ddlredirecttabid");
                    if (!Utils.IsNumeric(redirecttabid)) redirecttabid = TabId.ToString("");
                    Response.Redirect(Globals.NavigateURL(Convert.ToInt32(redirecttabid), "", param), true);
                    break;
            }

        }

        #endregion


    }

}
