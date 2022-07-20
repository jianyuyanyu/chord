﻿using dp2weixin.service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http;

namespace dp2weixinWeb.ApiControllers
{
    
    public class LibSettingApiController : ApiController
    {

        // 获取全部图书馆，按地区分类，用于终端用户选择图书馆
        // 返回Area集合，第一级Area表示地区，第二级Libs/LibModel表示下级的图书馆，是一个集合
        public IEnumerable<Area> GetAreaLib()
        {
            
            SessionInfo sessionInfo = (SessionInfo)HttpContext.Current.Session[WeiXinConst.C_Session_sessioninfo];
            //if (sessionInfo.ActiveUser == null)
            //{
            //    dp2WeiXinService.Instance.WriteDebug("提交流通API时，发现session失效了。");
            //}



            // 获取可访问的图书馆
            //List<Library> avaiblelibList = dp2WeiXinService.Instance.LibManager.GetLibraryByIds(sessionInfo.libIds);


            // 获取该微信帐户绑定了哪些图书馆帐户
            List<WxUserItem> list = WxUserDatabase.Current.Get(sessionInfo.WeixinId, null, -1);

            // 可显示的区域
            List<Area> areaList = new List<Area>();
            // 从所有区域中查找
            foreach (Area area in dp2WeiXinService.Instance._areaMgr._areas)
            {
                List<LibModel> libList = new List<LibModel>();
                foreach (LibModel lib in area.libs)
                {
                    lib.Checked = "";
                    lib.bindFlag = "";

                    // 如果是到期的图书馆，不显示出来
                    Library thisLib = dp2WeiXinService.Instance.LibManager.GetLibrary(lib.libId);//.GetLibById(lib.libId);
                    if (thisLib != null && thisLib.Entity.state == "到期")
                    {
                        continue;
                    }

                    ////如果不在可访问范围，不显示
                    //if (thisLib != null && avaiblelibList.IndexOf(thisLib) == -1)
                    //{
                    //    continue;
                    //}

                    // 如果从mongodb库没有找到图书馆，不显示
                    // 有可能是mongodb库删除，但配置文件还没有删除
                    if (thisLib == null)
                    {
                        dp2WeiXinService.Instance.WriteDebug("选择图书馆时，根据[" + lib.libId + "]未找到对应的图书馆");
                        continue;
                    }

                    // 加到显示列表
                    libList.Add(lib);

                    // 检查微信用户是否绑定了这个图书馆
                    WxUserItem tempUser = null;
                    //if (this.CheckIsBind(list, lib, out tempUser) == true)  //libs.Contains(lib.libId)
                    //{
                    //    if (tempUser.userName != WxUserDatabase.C_Public)
                    //        lib.bindFlag = " * ";
                    //}

                    // 当前绑定帐户的图书馆显示为勾中状态
                    if (sessionInfo.ActiveUser != null)
                    {
                        if (lib.libId == sessionInfo.ActiveUser.libId
                            && lib.libraryCode == sessionInfo.ActiveUser.bindLibraryCode)
                        {
                            lib.Checked = " checked ";
                        }
                    }
                }

                // 只有当有下级图书馆时，才显示地区
                if (libList.Count > 0)
                {
                    Area newArea = new Area();
                    newArea.name = area.name;
                    newArea.libs = libList;
                    areaList.Add(newArea);
                }

            }

            return areaList;
        }


    }
}