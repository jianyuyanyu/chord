﻿//#define LOG_REQUEST_SEARCH

using com.google.zxing;
using com.google.zxing.common;
using common;
using DigitalPlatform;
using DigitalPlatform.Interfaces;
using DigitalPlatform.IO;
using DigitalPlatform.Marc;
using DigitalPlatform.Message;
using DigitalPlatform.MessageClient;
using DigitalPlatform.Text;
using DigitalPlatform.Xml;
using Newtonsoft.Json;
using Senparc.Weixin;
using Senparc.Weixin.MP.AdvancedAPIs;
using Senparc.Weixin.MP.AdvancedAPIs.TemplateMessage;
using Senparc.Weixin.MP.CommonAPIs;
using Senparc.Weixin.MP.Containers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Xml;

namespace dp2weixin.service
{
    public class dp2WeiXinService
    {
        #region 常量

        // 通道常量
        public const string C_ConnName_TraceMessage = "_traceMessage";
        public const string C_ConnPrefix_Myself = "<myself>:";
        public const string C_ConnName_dp = "_dp_";
        public const string C_ConnName_CheckOfflineLibs = "_checkOfflineLibs";

        // 群组常量
        public const string C_Group_Bb = "gn:_lib_bb";
        public const string C_Group_Book = "gn:_lib_book";
        public const string C_Group_HomePage = "gn:_lib_homePage"; //图书馆介绍
        public const string C_Group_PatronNotity = "gn:_patronNotify";
        // 公司公告
        public const string C_Group_dp_home = "gn:_dp_home";

        // 消息权限
        public const string C_Right_SetBb = "_wx_setbb";
        public const string C_Right_SetBook = "_wx_setbook";
        public const string C_Right_SetHomePage = "_wx_setHomePage";
        // 接收警告通知
        public const string C_Right_GetWarning = "_wx_getWarning";

        // 2021/8/2 调试态权限
        public const string C_Right_Debug = "_wx_debug";

        // 超级管理员标志
        public const string C_Supervisor = "_supervisor_";

        public const string C_Session_Supervisor = "supervisor";

        //// 日志分级
        public const int C_LogLevel_1 = 1;
        public const int C_LogLevel_2 = 2;
        public const int C_LogLevel_3 = 3;

        // 公众号名称
        public const string C_gzh_ilovelibrary = "ilovelibrary";
        public const string C_gzh_dp = "dp";
        public const string C_gzh_dpcomm = "dpcomm";

        #endregion

        #region 成员变量
        // 日志级别
        public int LogLevel = 1;

        // 微信数据目录
        public string _weiXinDataDir = "";
        public string _weiXinLogDir = "";
        public string _cfgFile = "";      // 配置文件

        // 图书馆配置文件
        public string _libCfgFile = "";
        public AreaManager _areaMgr = null;

        // 定长字段配置
        public MarcFixedFieldManager _marcFixedFieldMgr = null;

        // dp2服务器地址与代理账号
        public string _dp2MServerUrl = "";
        public string _userNameWeixin = "";
        public string _password = "";

        public string _monodbConnectionString = "";
        public string _monodbPrefixString = "";
        public bool _bTrace = false;

        // 密码钥匙
        public string EncryptKey = "";//安全性，删除，内部保存。

        // 微信公众号信息
        public GzhContainer _gzhContainer = null;

        public string AppletAppId = "";
        public string AppletAppSecret = "";

        // 2023/5/24加
        private string _messageLinkUrl = "";


        // dp2消息处理类
        MsgRouter _msgRouter = new MsgRouter();

        /// <summary>
        /// 通道
        /// </summary>
        MessageConnectionCollection _channels = new MessageConnectionCollection();
        public MessageConnectionCollection Channels
        {
            get
            {
                return this._channels;
            }
        }

        // 打开 tracing的微信用户
        public List<WxUserItem> TracingOnUserList = new List<WxUserItem>();


        // 公众号后台管理线程
        ManagerThread _managerThread = new ManagerThread();

        // 图书馆集合类
        public LibraryManager LibManager = new LibraryManager();

        #endregion

        #region 检查一个分馆是否需要自动加前缀

        // 缓冲中存放已经明确知道是否自动加前缀的分馆
        public Hashtable transformBarcodeLibs = new Hashtable();

        public int CheckIsTransformBarcode(string libId, string libraryCode)
        {
            if (String.IsNullOrEmpty(libraryCode) == true)
                return 0;

            string fullId = libId + "~" + libraryCode;

            // 如果已在缓冲中，直接使用
            if (this.transformBarcodeLibs.ContainsKey(fullId) == true)
            {
                return (int)this.transformBarcodeLibs[fullId];
            }

            return -1;
        }

        public void SetTransformBarcode(string libId, string libraryCode, int value)
        {
            if (String.IsNullOrEmpty(libraryCode) == true || string.IsNullOrEmpty(libId) == true)
                return;

            string fullId = libId + "~" + libraryCode;

            this.transformBarcodeLibs[fullId] = value;
        }

        #endregion

        #region 图书馆参于检索的数据库

        public Hashtable LibDbs = new Hashtable();

        public int GetDbNames(LibEntity lib, out string dbnames, out string strError)
        {
            strError = "";
            dbnames = "";
            // 优先认配置的
            if (String.IsNullOrEmpty(lib.searchDbs) == false)
            {
                dbnames = lib.searchDbs;
                return 1;
            }

            string libId = lib.id;
            // 如果已在缓冲中，直接使用
            if (this.LibDbs.ContainsKey(libId) == true)
            {
                dbnames = (string)this.LibDbs[libId];
                return 1;
            }

            // 调服务器api，获得opac检索库
            //string dbnames="";
            int nRet = this.GetDbNamesFromLib(lib, out dbnames, out strError);
            if (nRet == -1)
                return -1;

            // 如果没有配置设全部
            if (dbnames == "")
                dbnames = "<全部>";


            // 设到缓冲里
            this.LibDbs[libId] = dbnames;
            return 1;
        }

        public int GetDbNamesFromLib(LibEntity lib, out string dbnames, out string strError)
        {
            dbnames = "";
            strError = "";
            List<string> dataList = new List<string>();
            int nRet = this.GetSystemParameter(lib, "virtual", "def", out dataList, out strError);
            if (nRet == -1 || nRet == 0)
            {
                return nRet;
            }

            string xml = dataList[0];
            XmlDocument dom = new XmlDocument();
            dom.LoadXml(xml);
            XmlNode root = dom.DocumentElement;
            XmlNodeList dbList = root.SelectNodes("database");
            foreach (XmlNode node in dbList)
            {
                if (dbnames != "")
                    dbnames += ",";

                string name = DomUtil.GetAttr(node, "name");

                XmlNode captionNode = node.SelectSingleNode("caption[@lang='zh']");
                if (captionNode != null)
                    name = DomUtil.GetNodeText(captionNode);

                dbnames += name;
            }


            return 1;
        }
        List<biblioDbGroup> _dbs = null;

        public string GetBiblioDbName(LibEntity lib, string type, string name)
        {
            biblioDbGroup db = this.GetDb(lib, type, name);
            if (db != null)
                return db.biblioDbName;


            return "";
        }
        public biblioDbGroup GetDb(LibEntity lib,string type, string name)
        {
            string strError = "";

            if (this._dbs == null)
            {
                int nRet = this.GetDbs(lib, out strError);
                if (nRet == -1)
                    return null;
            }

            if (this._dbs == null || this._dbs.Count == 0)
                return null;

            foreach(biblioDbGroup one  in this._dbs)
            {
                if (type == "item" && one.itemDbName == name)
                { 
                        return one;
                }

                if (type == "biblio" && one.biblioDbName == name)
                {
                    return one;
                }
            }

            return null;
        }

        // 获取系统中的所有数据库
        public int GetDbs(LibEntity lib,out string strError)
        {
                strError = "";
            List<string> dataList = null;
            // 获取书目库
            int nRet = this.GetSystemParameter(lib,
                "system",
                "biblioDbGroup",
                out dataList,
                out strError);
            if (nRet == -1 || nRet == 0)
            {
                return -1;
            }

            //<database biblioDbName="中文图书" syntax="unimarc"
            // orderDbName="中文图书订购" commentDbName="中文图书评注"
            // inCirculation="true" role="catalogWork" itemDbName="中文图书实体"  issueDbName="中文期刊期" >
            string xml = dataList[0];
            xml = "<root>" + xml + "</root>";
            XmlDocument dom = new XmlDocument();
            dom.LoadXml(xml);
            XmlNodeList list = dom.DocumentElement.SelectNodes("database");
            this._dbs = new List<biblioDbGroup>();
            foreach (XmlNode node in list)
            {

                biblioDbGroup one = new biblioDbGroup();
                one.biblioDbName = DomUtil.GetAttr(node, "biblioDbName");
                one.itemDbName = DomUtil.GetAttr(node, "itemDbName");
                one.orderDbName = DomUtil.GetAttr(node, "orderDbName");
                one.commentDbName = DomUtil.GetAttr(node, "commentDbName");
                one.issueDbName = DomUtil.GetAttr(node, "issueDbName");
                one.syntax = DomUtil.GetAttr(node, "syntax");
                one.inCirculation = DomUtil.GetAttr(node, "inCirculation");
                one.role = DomUtil.GetAttr(node, "role");
                this._dbs.Add(one);
            }

            return 0;
        }

        public class biblioDbGroup
        {
            //<database biblioDbName="中文图书" syntax="unimarc"
            // orderDbName="中文图书订购" commentDbName="中文图书评注"
            // inCirculation="true" role="catalogWork" itemDbName="中文图书实体"  issueDbName="中文期刊期" >
            public string biblioDbName { get; set; }

            public string itemDbName { get; set; }
            public string orderDbName { get; set; }
            public string commentDbName { get; set; }
            public string issueDbName { get; set; }
            public string syntax { get; set; }

            public string inCirculation { get; set; }
            public string role { get; set; }
        }

        public int GetSystemParameter(LibEntity lib,
            string catgory,
            string name,
            out List<string> dataList,
            out string strError)
        {
            // 20170116 使用代理账户
            LoginInfo loginInfo = new LoginInfo("", false);

            return this.GetInfo(lib,
                loginInfo,
                "getSystemParameter",
                catgory,
                name,
                out dataList,
                out strError);
        }


        /// <summary>
        /// 获取馆藏地
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="user"></param>
        /// <param name="location"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public int GetLocation(string libId,
            WxUserItem user,
            out string location,
            out string error)
        {
            location = "";
            error = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                error = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            LoginInfo loginInfo = GetLoginInfo(user);


            List<string> dataList = null;
            int nRet = this.GetInfo(lib,
                loginInfo,
                "getSystemParameter",
                "circulation",
                "locationTypes",
                out dataList,
                out error);
            if (nRet == -1 || nRet == 0)
                return -1;
            location = dataList[0];
            return 1;
        }

        /// <summary>
        /// 得到图书类型
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="user"></param>
        /// <param name="bookType"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public int GetBookType(string libId,
            WxUserItem user,
            out string bookType,
            out string error)
        {
            bookType = "";
            error = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                error = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            LoginInfo loginInfo = GetLoginInfo(user);
            List<string> dataList = null;
            int nRet = this.GetInfo(lib,
                loginInfo,
                "getSystemParameter",
                "_valueTable",
                "bookType",
                out dataList,
                out error);
            if (nRet == -1 || nRet == 0)
                return -1;

            bookType = dataList[0];
            return 1;
        }


        /// <summary>
        /// 得到读者类型
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="user"></param>
        /// <param name="readerType"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public int GetReaderType(string libId,
            WxUserItem user,
            out string readerType,
            out string error)
        {
            readerType = "";
            error = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                error = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            LoginInfo loginInfo = GetLoginInfo(user);
            List<string> dataList = null;
            int nRet = this.GetInfo(lib,
                loginInfo,
                "getSystemParameter",
                "_valueTable",
                "readerType",
                out dataList,
                out error);
            if (nRet == -1 || nRet == 0)
                return -1;

            readerType = dataList[0];
            return 1;
        }



        /// <summary>
        /// 根据本地用户，获取loginInfo
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public LoginInfo GetLoginInfo(WxUserItem user)
        {
            // 如果传入的user为null，则返回默认的代码帐户capo
            if (user == null)
                return new LoginInfo("", false);

            string userName = "";
            bool isPatron = false;
            if (user.type == WxUserDatabase.C_Type_Worker)
            {
                userName = user.userName;
                isPatron = false;
            }
            else
            {
                userName = user.readerBarcode;
                isPatron = true;
            }
            return new LoginInfo(userName, isPatron);
        }


        public int GetLibName(string capoUserName,
            out string libName,
            out string strError)
        {
            libName = "";
            strError = "";
            List<string> dataList = new List<string>();

            LibEntity lib = new LibEntity();
            lib.capoUserName = capoUserName;
            int nRet = this.GetSystemParameter(lib, "library", "name", out dataList, out strError);
            if (nRet == -1 || nRet == 0)
            {
                if (strError == "TargetNotFound")
                    strError = "账户 " + capoUserName + " 在dp2mserver服务器中不存在 或者对应的桥接服务器失去联系，无法获得图书馆名称。";
                return -1;
            }

            libName = dataList[0];
            return 1;
        }

        #endregion

        #region 单一实例

        static dp2WeiXinService _instance;
        private dp2WeiXinService()
        {
        }
        private static object _lock = new object();
        static public dp2WeiXinService Instance
        {
            get
            {
                if (null == _instance)
                {
                    lock (_lock)  //线程安全的
                    {
                        _instance = new dp2WeiXinService();
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region 初始化

        public void Init(string dataDir)
        {
            string strError = "";
            int nRet = 0;

            this._weiXinDataDir = dataDir;

            this._cfgFile = this._weiXinDataDir + "\\" + "weixin.xml";
            if (File.Exists(this._cfgFile) == false)
            {
                throw new Exception("配置文件" + this._cfgFile + "不存在。");
            }



            // 日志目录
            this._weiXinLogDir = this._weiXinDataDir + "/log";
            if (!Directory.Exists(_weiXinLogDir))
            {
                Directory.CreateDirectory(_weiXinLogDir);
            }


            XmlDocument dom = new XmlDocument();
            dom.Load(this._cfgFile);
            XmlNode root = dom.DocumentElement;



            // 取出mserver服务器配置信息
            XmlNode nodeDp2mserver = root.SelectSingleNode("//dp2mserver");
            this._dp2MServerUrl = DomUtil.GetAttr(nodeDp2mserver, "url");// WebConfigurationManager.AppSettings["dp2MServerUrl"];
            this._userNameWeixin = DomUtil.GetAttr(nodeDp2mserver, "username");//WebConfigurationManager.AppSettings["userName"];
            this._password = DomUtil.GetAttr(nodeDp2mserver, "password");//WebConfigurationManager.AppSettings["password"];
            this.EncryptKey = DomUtil.GetAttr(nodeDp2mserver, "EncryptKey");

            if (string.IsNullOrEmpty(this._password) == false)// 解密
                this._password = Cryptography.Decrypt(this._password, this.EncryptKey);

            string trace = DomUtil.GetAttr(nodeDp2mserver, "trace");
            if (trace.ToLower() == "true")
                this._bTrace = true;

            // 取出微信公众号配置信息
            this._gzhContainer = new GzhContainer(dom);

            // 2022/9/20 获取小程序配置信息
            this.AppletAppId =DomUtil.GetAttr(root, "applet", "appId");
            this.AppletAppSecret= DomUtil.GetAttr(root, "applet", "appSecret");


            // 消息详情url
            GzhCfg gzh = dp2WeiXinService.Instance._gzhContainer.GetDefault();//.GetByAppId(this.AppId);
            if (gzh == null)
            {
                throw new Exception("未找到默认的公众号配置");
            }
            //this._messageLinkUrl = dp2WeiXinService.Instance.GetOAuth2Url(gzh,HttpUtility.UrlEncode("Library/Message"));

            this._messageLinkUrl = dp2WeiXinService.Instance.GetOAuth2Url_part(gzh)+"[message]";


            //

            // mongo配置
            XmlNode nodeMongoDB = root.SelectSingleNode("mongoDB");
            this._monodbConnectionString = DomUtil.GetAttr(nodeMongoDB, "connectionString");
            if (String.IsNullOrEmpty(this._monodbConnectionString) == true)
            {
                throw new Exception("尚未配置mongoDB节点的connectionString属性");
            }
            this._monodbPrefixString = DomUtil.GetAttr(nodeMongoDB, "instancePrefix");

            // 打开图书馆账号库与用户库 todo是放在这里打开 还是 在LibraryManager类中打开？
            WxUserDatabase.Current.Open(this._monodbConnectionString, this._monodbPrefixString);
            LibDatabase.Current.Open(this._monodbConnectionString, this._monodbPrefixString);
            UserMessageDb.Current.Open(this._monodbConnectionString, this._monodbPrefixString);

            // 挂上登录事件
            _channels.Login -= _channels_Login;
            _channels.Login += _channels_Login;

            // 将mongodb库中的图书馆加载到内存中
            nRet = this.LibManager.Init(out strError);
            if (nRet == -1)
                throw new Exception("加载图书馆到内存出错：" + strError);

            //装载libcfg.xml，初始化地区和图书馆关系
            this._libCfgFile = this._weiXinDataDir + "\\" + "libcfg.xml";
            if (File.Exists(this._libCfgFile) == false)
            {
                XmlDocument dom1 = new XmlDocument();
                dom1.LoadXml("<root/>");
                dom1.Save(this._libCfgFile);
                //throw new Exception("配置文件" + this.libCfgFile + "不存在。");
            }
            this._areaMgr = new AreaManager();
            nRet = _areaMgr.init(this._libCfgFile, out strError);
            if (nRet == -1)
                throw new Exception(strError);

            // 把配置的定制字段文件装载到内存结构，用于简编功能
            string fixedFieldFile = this._weiXinDataDir + "\\" + "MarcFixedFieldDef.xml";
            this._marcFixedFieldMgr = new MarcFixedFieldManager();
            if (File.Exists(fixedFieldFile) == true)
            {
                nRet = _marcFixedFieldMgr.init(fixedFieldFile, out strError);
                if (nRet == -1)
                    throw new Exception(strError);
            }

            // 初始化接口类
            nRet = this.InitialExternalMessageInterfaces(dom, out strError);
            if (nRet == -1)
                throw new Exception("初始化接口配置信息出错：" + strError);

            // 加载打开监控功能微信用户
            LoadTracingUser();


            ////全局只需注册一次
            //AccessTokenContainer.Register(this.weiXinAppId, this.weiXinSecret);

            if (_bTrace == true)
            {
                if (this.Channels.TraceWriter != null)
                    this.Channels.TraceWriter.Close();
                StreamWriter sw = new StreamWriter(Path.Combine(this._weiXinDataDir, "trace.txt"));
                sw.AutoFlush = true;
                _channels.TraceWriter = sw;
            }

            // 消息处理类
            this._msgRouter.SendMessageEvent -= _msgRouter_SendMessageEvent;
            this._msgRouter.SendMessageEvent += _msgRouter_SendMessageEvent;
            this._msgRouter.Start(this._channels,
                this._dp2MServerUrl,
                C_Group_PatronNotity);

            // 启动管理线程
            this._managerThread.WeixinService = this;
            this._managerThread.BeginThread();

        }

        // 加载打开了监控的帐户
        public void LoadTracingUser()
        {
            dp2WeiXinService.Instance.TracingOnUserList.Clear();

            List<WxUserItem> userList = WxUserDatabase.Current.GetTracingUsers();

            foreach (WxUserItem user in userList)
            {

                // 加到内存集合
                dp2WeiXinService.Instance.TracingOnUserList.Add(user);//[user.weixinId] = tracingOnUser;
            }
        }

        // 根据条件得到内存的监控消息的对象
        private WxUserItem GetTraceUser(string weixinId,
            string libId,
            string libraryCode)
        {
            foreach (WxUserItem user in this.TracingOnUserList)
            {
                if (user.weixinId == weixinId
                    && user.libId == libId
                    && user.libraryCode == libraryCode)
                {
                    return user;
                }
            }
            return null;
        }


        // 更新内存中的监控对象
        public void UpdateTracingUser(WxUserItem user)
        {
            // 先从内存中查找是否已有记录
            WxUserItem traceUser = this.GetTraceUser(user.weixinId,
                user.libId,
                user.libraryCode);

            // 先删除
            if (traceUser != null)
            {
                // 从内存中移除
                dp2WeiXinService.Instance.TracingOnUserList.Remove(traceUser);
            }

            // 关闭监控
            if (user.tracing != "on" && user.tracing != "on -mask")
            {
                return;
            }


            // 打开监控功能的情况
            if (user.tracing == "on" || user.tracing == "on -mask")
            {
                this.TracingOnUserList.Add(user);
            }
        }


        //Patron/PersonalInfo,Account/Index,Account/ResetPassword,Library/Home
        public string GetOAuth2Url(GzhCfg gzh, string func)
        {
            func = func.Replace("/", "%2f");

            // auth2地址
            string url = "https://open.weixin.qq.com/connect/oauth2/authorize"
                + "?appid=" + gzh.appId
                + "&redirect_uri=http%3a%2f%2fdp2003.com%2fi%2f" + func
                + "&response_type=code"
                + "&scope=snsapi_base"
                + "&state=" + gzh.appName
                + "#wechat_redirect";

            this.WriteDebug(url);
            return url;
        }

        public string GetOAuth2Url_part(GzhCfg gzh)
        {
            // auth2地址
            string url = "https://open.weixin.qq.com/connect/oauth2/authorize"
                + "?appid=" + gzh.appId
                + "&response_type=code"
                + "&scope=snsapi_base"
                + "&state=" + gzh.appName;

            this.WriteDebug(url);
            return url;
        }

        public string GetOAuth2Url_func(string url, string func)
        {
            func = func.Replace("/", "%2f");

            // auth2地址
            return url+ "&redirect_uri=http%3a%2f%2fdp2003.com%2fi%2f" + func
                + "#wechat_redirect";

        }






        public void Close()
        {
            if (this._msgRouter != null)
            {
                this._msgRouter.SendMessageEvent -= _msgRouter_SendMessageEvent;
                this._msgRouter.Stop();
            }
            if (this.Channels != null)
            {
                this.Channels.Login -= _channels_Login;
                if (this.Channels.TraceWriter != null)
                    this.Channels.TraceWriter.Close();
            }

            this.WriteDebug("走到close()");
        }

        #endregion

        #region 本方账号登录


        // 公众号登录dp2mserver的帐号
        // 如果连接串是<myself>开头，后面跟着libid，则表示用lib对应的weixin_xxx帐户，其它情况用全局weixin帐户
        void _channels_Login(object sender, LoginEventArgs e)
        {
            MessageConnection connection = sender as MessageConnection;

            string connName = connection.Name;
            string prefix = C_ConnPrefix_Myself;
            if (connName.Length > prefix.Length && connName.Substring(0, prefix.Length) == prefix)
            {
                // 需要使用当前图书馆的微信端本方帐户
                string libId = connName.Substring(prefix.Length);

                // 全局的联系我们
                if (libId == C_ConnName_dp)
                {
                    e.UserName = GetUserName();
                    if (string.IsNullOrEmpty(e.UserName) == true)
                        throw new Exception("尚未指定用户名，无法进行登录");

                    e.Password = GetPassword();
                }
                else
                {
                    LibEntity lib = this.GetLibById(libId);
                    if (lib == null)
                    {
                        throw new Exception("异常的情况:根据id[" + libId + "]未找到图书馆对象。");
                    }

                    e.UserName = lib.wxUserName;
                    if (string.IsNullOrEmpty(e.UserName) == true)
                        throw new Exception("尚未指定微信本方用户名，无法进行登录");
                    e.Password = lib.wxPassword;
                }
            }
            else
            {
                e.UserName = GetUserName();
                if (string.IsNullOrEmpty(e.UserName) == true)
                    throw new Exception("尚未指定用户名，无法进行登录");

                e.Password = GetPassword();
            }

            e.Parameters = "notes=" + HttpUtility.UrlEncode(connection.Name); //"propertyList=biblio_search,libraryUID=xxx";
        }


        string GetUserName()
        {
            return this._userNameWeixin;
        }
        string GetPassword()
        {
            return this._password;
        }

        #endregion

        #region 设置dp2mserver信息

        public int SetDp2mserverInfo(string dp2mserverUrl,
            string userName,
            string password,
            string mongodbConnection,
            string mongodbPrefix,
            out string strError)
        {
            strError = "";

            string oldUserName = this.GetUserName();
            string oldPassword = this._password;

            // 先检查下地址与密码是否可用，如不可用，不保存
            try
            {
                this._userNameWeixin = userName;
                this._password = password;
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                  dp2mserverUrl,
                    Guid.NewGuid().ToString()).Result;
            }
            catch (AggregateException ex)
            {
                strError = "测试服务器连接不成功：" + MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = "测试服务器连接不成功：" + ex.Message;
                goto ERROR1;
            }

            XmlDocument dom = new XmlDocument();
            dom.Load(this._cfgFile);
            XmlNode root = dom.DocumentElement;

            // 设置mserver服务器配置信息
            XmlNode nodeDp2mserver = root.SelectSingleNode("dp2mserver");
            if (nodeDp2mserver == null)
            {
                nodeDp2mserver = dom.CreateElement("dp2mserver");
                root.AppendChild(nodeDp2mserver);
            }
            DomUtil.SetAttr(nodeDp2mserver, "url", dp2mserverUrl);
            DomUtil.SetAttr(nodeDp2mserver, "username", userName);
            string encryptPassword = Cryptography.Encrypt(password, this.EncryptKey);
            DomUtil.SetAttr(nodeDp2mserver, "password", encryptPassword);
            dom.Save(this._cfgFile);

            // 更新内存的信息
            this._dp2MServerUrl = dp2mserverUrl;
            this._userNameWeixin = userName;
            this._password = password;

            return 0;

        ERROR1:
            // 还原原来的值
            this._userNameWeixin = oldUserName;
            this._password = oldPassword;
            return -1;
        }

        public void Getdp2mserverInfo(out string dp2mserverUrl,
            out string userName,
            out string password,
            out string mongodbConnection,
            out string mongodbPrefix)
        {
            dp2mserverUrl = "";
            userName = "";
            password = "";
            mongodbConnection = "";
            mongodbPrefix = "";

            XmlDocument dom = new XmlDocument();
            dom.Load(this._cfgFile);
            XmlNode root = dom.DocumentElement;

            // 设置mserver服务器配置信息
            XmlNode nodeDp2mserver = root.SelectSingleNode("dp2mserver");
            if (nodeDp2mserver != null)
            {
                dp2mserverUrl = DomUtil.GetAttr(nodeDp2mserver, "url");
                userName = DomUtil.GetAttr(nodeDp2mserver, "username");
                password = DomUtil.GetAttr(nodeDp2mserver, "password");
                if (string.IsNullOrEmpty(password) == false)// 解密
                    password = Cryptography.Decrypt(this._password, this.EncryptKey);
            }

            // 设置mongoDB
            XmlNode nodeMongoDB = root.SelectSingleNode("mongoDB");
            if (nodeMongoDB != null)
            {
                mongodbConnection = DomUtil.GetAttr(nodeMongoDB, "connectionString");
                mongodbPrefix = DomUtil.GetAttr(nodeMongoDB, "instancePrefix");
            }
        }

        public void GetSupervisorAccount(out string username,
            out string password)
        {
            username = "";
            password = "";

            XmlDocument dom = new XmlDocument();
            dom.Load(this._cfgFile);
            XmlNode root = dom.DocumentElement;

            // 设置mserver服务器配置信息
            XmlNode nodeSupervisor = root.SelectSingleNode("Supervisor");
            if (nodeSupervisor != null)
            {
                username = DomUtil.GetAttr(nodeSupervisor, "username");
                password = DomUtil.GetAttr(nodeSupervisor, "password");
                if (string.IsNullOrEmpty(password) == false)// 解密
                    password = Cryptography.Decrypt(password, this.EncryptKey);
            }
        }

        #endregion

        #region 消息处理

        // 处理收到的消息
        void _msgRouter_SendMessageEvent(object sender, SendMessageEventArgs e)
        {
            MessageRecord record = e.Message;
            if (record == null)
            {
                this.WriteErrorLog("传过来的e.Message为null");
                return;
            }

            this.WriteDebug("========");
            this.WriteDebug("收到消息[" + record.id + "]");
            this.WriteDebug2("publishTime=" + record.publishTime);
            try
            {
                LibEntity lib = null;
                if (string.IsNullOrEmpty(record.userName) == false)
                {
                    //根据capo_xxx找到对应的图书馆
                    lib = LibDatabase.Current.GetLibByCapoUserName(record.userName);

                    // 没有找到图书馆
                    if (lib == null)
                    {
                        this.WriteErrorLog("SendMessageEvent(),未找到[" + record.userName + "]对应的图书馆。");
                        return; // 2020/8/3 发现这里没写return
                    }

                    // 如果图书馆的状态是已到期，则不继续发送消息
                    if (lib.state == C_State_Expire)
                    {
                        this.WriteErrorLog("" + record.userName + " 已到期，不支持将消息发送到用户微信，自行删除。");
                        return;
                    }
                }
                else
                {
                    this.WriteErrorLog("SendMessageEvent()异常：消息[" + record.id + "]传过来的userName为空。");
                    return; // 2020/8/3 发现这里没写return
                }


                string strError = "";
                /// <returns>
                /// -1 不符合条件，不处理
                /// 1 成功
                /// </returns>
                int nRet = this.InternalDoMessage(record,
                    lib,
                    out strError);
                if (nRet == -1)
                {
                    this.WriteErrorLog("消息[" + record.id + "]处理失败:" + strError);
                }
                else
                {
                    this.WriteDebug("消息[" + record.id + "]处理完成。");
                    this.WriteDebug("========");
                }

            }
            catch (Exception ex)
            {
                this.WriteErrorLog("[" + record.id + "]处理异常：" + ex.Message);
            }
        }

        /// <summary>
        /// 内部处理消息
        /// </summary>
        /// <param name="record"></param>
        /// <param name="strError"></param>
        /// <returns>
        /// -1 出错，或不符合处理条件
        /// 0 未绑定微信id，未处理
        /// 1 成功
        /// </returns>
        public int InternalDoMessage(MessageRecord record,
            LibEntity lib,
            out string strError)
        {
            strError = "";

            string id = record.id;
            string data = record.data;
            string[] group = record.groups;
            string create = record.creator;

            // 将消息体加载到dom
            XmlDocument dataDom = new XmlDocument();
            try
            {
                dataDom.LoadXml(data);
            }
            catch (Exception ex)
            {
                strError = "加载消息data到xml出错：" + ex.Message + "\r\n" + data;
                return -1;
            }

            //检查是不是patronNotify通知
            //<root>
            //    <type>patronNotify</type>
            //    <recipient>R0000001@LUID:62637a12-1965-4876-af3a-fc1d3009af8a</recipient>
            //    <mime>xml</mime>
            //    <body>...</body>
            //</root>
            XmlNode nodeType = dataDom.DocumentElement.SelectSingleNode("type");
            if (nodeType == null)
            {
                strError = "尚未定义<type>节点";
                return -1;
            }
            string topType = DomUtil.GetNodeText(nodeType);
            if (topType != "patronNotify") //只处理通知消息
            {
                strError = "<type>节点值不是patronNotify。";
                return -1;
            }

            // 检查是否存在body
            XmlNode nodeBody = dataDom.DocumentElement.SelectSingleNode("body");
            if (nodeBody == null)
            {
                strError = "data中不存在body节点";
                return -1;
            }

            //从body元素内容里，查看各种消息类型
            /*
            body元素里面是预约到书通知记录(注意这是一个字符串，需要另行装入一个XmlDocument解析)，其格式如下：
            <?xml version="1.0" encoding="utf-8"?>
            <root>
                <type>预约到书通知</type>
                ...
            </root
            */
            string strBody = nodeBody.InnerText; //消息体
            XmlDocument bodyDom = new XmlDocument();
            try
            {
                bodyDom.LoadXml(strBody);//.InnerXml); 
            }
            catch (Exception ex)
            {
                strError = "加载消息data中的body到xml出错：" + ex.Message;
                return -1;
            }

            //===根据type处理各类消息===
            int nRet = 0;

            XmlNode root = bodyDom.DocumentElement;
            XmlNode typeNode = root.SelectSingleNode("type");
            if (typeNode == null)
            {
                strError = "消息data的body中未定义type节点。";
                return -1;
            }


            string strType = DomUtil.GetNodeText(typeNode);
            this.WriteDebug("a消息类型为[" + strType + "]");

            //处理每类消息
            if (strType == "工作人员账户变动")
            {
                nRet = this.UpdateWorker(lib, bodyDom, out strError);
                return 0;// nRet;
            }
            if (strType == "读者记录变动")
            {
                nRet = this.UpdatePatron(lib, bodyDom, out strError);
                return 0;// nRet;
            }
            if (strType == "登录验证码")//opac找回密码，也是转到这儿来的
            {
                /*
                 <root>
                    <type>登录验证码</type>
                    <phoneNumber>123</phoneNumber>
                    <text>登录验证码为9999。十分钟以后失效</text>
                </root>
                 */
                nRet = this.SendLoginVerifyCode(lib, bodyDom, out strError);
                return nRet;
            }

            //===以下都是关于读者的微信通知====
            string libName = "";
            string libId = "";
            if (lib != null)
            {
                libName = lib.libName;
                libId = lib.id;
            }

            // 得到此读者对应的绑定的微信id
            string patronBarcode = "";
            string patronName = "";
            string libraryCode = "";

            // 消息格式
            /*
                        <root>
                            <type>超期通知</type>
                            <patronRecord>
                                <barcode>R0000001</barcode>
                                <readerType>本科生</readerType>
                                <name>张三</name>
                                <refID>be13ecc5-6a9c-4400-9453-a072c50cede1</refID>
                                <department>数学系</department>
                                <address>address</address>
                                <cardNumber>C12345</cardNumber>
                                <refid>8aa41a9a-fb42-48c0-b9b9-9d6656dbeb76</refid>
                                <email>email:renyh@dp2003.com</email>
                                <tel>13641016400</tel>
                                <idCardNumber>500103197408256419</idCardNumber>
                            </patronRecord>
                            <items overdueCount='1' normalCount='0'>
                                <item barcode='' 
                            summary='船舶柴油机 / 聂云超主编. -- ISBN 7-...'   
                            timeReturning='2016/5/18' 
                                        overdue='已超期 31 天' 
                                        overdueType='overdue' />
                            </items>
                            <text>您借阅的下列书刊：船舶柴油机 / 聂云超主编. -- ISBN 7-... 应还日期: 2016/5/18 已超期 31 天</text>
                        </root>
            */
            // 找到读者节点
            XmlNode patronRecordNode = root.SelectSingleNode("patronRecord");
            if (patronRecordNode == null)
            {
                strError = "消息data尚未定义<patronRecordNode>节点。";
                return -1;
            }

            // barcode
            XmlNode node = patronRecordNode.SelectSingleNode("barcode");
            if (node != null)
                patronBarcode = DomUtil.GetNodeText(node);

            // name
            node = patronRecordNode.SelectSingleNode("name");
            if (node != null)
                patronName = DomUtil.GetNodeText(node);

            // 分馆代码，libraryCode
            node = patronRecordNode.SelectSingleNode("libraryCode");
            if (node != null)
                libraryCode = DomUtil.GetNodeText(node);

            // 如果当馆图书馆配置了第三方接口，则把消息转发给第三方
            LibModel libCfg = this._areaMgr.GetLibCfg(lib.id, libraryCode);
            if (libCfg != null && string.IsNullOrEmpty(libCfg.noticedll) == false)
            {
                string tempInfo = "";

                nRet = this.TransNotice(strBody, libCfg.noticedll, out tempInfo,
                    out strError);
                if (nRet == -1)
                {
                    WriteErrorLog("向" + libCfg.noticedll + "转发'" + strType + "'通知出错:" + strError + "[" + tempInfo + "]");
                }
                else
                {
                    WriteDebug("向" + libCfg.noticedll + "转发'" + strType + "'通知成功。[" + tempInfo + "]");
                    //WriteDebug(strBody);
                }
            }

            // 2024/12/23 加固程序，消息传过来的读者证条码可能为空，为空的话，不能去库中匹配，要不会匹配到所有读者
            List<WxUserItem> bindPatronList = new List<WxUserItem>();
            if (string.IsNullOrEmpty(patronBarcode) == true)
            {
                this.WriteDebug("patronBarcode为空，无法匹配绑定该读者的帐号。");
            }
            else
            {
                // 从微信本地库获取有多少用户绑定这个读者，给绑定这位读者的用户都要发通知
                 bindPatronList = WxUserDatabase.Current.Get("",
                    libId,
                    libraryCode,  //null, 2020/8/3发现这里传的null，应该是传librarycode
                    WxUserDatabase.C_Type_Patron,
                     patronBarcode,
                     "",
                     true);

                // 写一下日志，2020/8/1发现会收到两条消息
                if (bindPatronList.Count > 0)
                {
                    string temp = "";
                    foreach (WxUserItem u in bindPatronList)
                    {
                        temp += "weixinid=[" + u.weixinId + "],id=[" + u.id + "],readerBarcode=[" + u.readerBarcode + "],readerName=[" + u.readerName + "]\r\n";
                    }
                    this.WriteDebug("根据patronBarcode=[" + patronBarcode + "]从本地库找到[" + bindPatronList.Count + "]条绑定了该读者帐号，详情如下：\r\n" + temp);
                }
                else
                {
                    this.WriteDebug("根据patronBarcode=[" + patronBarcode + "]从本地库找到[0]条绑定了该读者帐号。");
                }
            }




            // 2021/8/3 屏蔽读者信息,配置在libcfg中
            bool send2PatronIsMask = false;
            string maskDef = this.GetMaskDef(libId, libraryCode, out send2PatronIsMask);

            // patronInfo = "";  //姓名 证条码号（图书馆/分馆）
            string fullPatronName = this.GetFullPatronName(patronName, patronBarcode, libName, libraryCode, false, maskDef);
            string markFullPatronName = this.GetFullPatronName(patronName, patronBarcode, libName, libraryCode, true, maskDef);


            // 得到这个馆绑定的工作人员，且工作人员打开tracing功能
            List<WxUserItem> workerList = this.GetTraceUsers(libId, libraryCode);
            // 写一下日志，2020/8/1发现会收到两条消息
            if (workerList.Count > 0)
            {
                string temp = "";
                foreach (WxUserItem u in workerList)
                {
                    temp += "weixinid=[" + u.weixinId + "],id=[" + u.id + "],userName=[" + u.userName + "]\r\n";
                }
                this.WriteDebug("从本地库找到[" + workerList.Count + "]条打开监控功能的工作人员，详情如下：\r\n" + temp);
            }
            else
            {
                this.WriteDebug("从本地库找到[0]条打开监控功能的工作人员。");
            }


            // 没有绑定这位读者的用户,也没有打开tracing的工作人员，不处理消息
            if (bindPatronList.Count == 0 && workerList.Count == 0)
            {
                this.WriteDebug("没有找到绑定该读者帐户的用户,也没有打开tracing的工作人员，所以没有发消息的接收对象。");
                return 0;
            }



            // 根据类型发送不同的模板消息
            if (strType == "预约到书通知")
            {
                nRet = this.SendArrived(bodyDom,
                    lib,//libName,
                    bindPatronList,
                    fullPatronName,
                    markFullPatronName,
                    workerList,
                    send2PatronIsMask,
                    out strError);
            }
            else if (strType == "超期通知")
            {
                nRet = this.SendCaoQi(bodyDom,
                    libName,
                    bindPatronList,
                    fullPatronName,
                    markFullPatronName,
                    workerList,
                    send2PatronIsMask,
                    out strError);
            }
            else if (strType == "召回通知")  //召回通知 20220622加
            {
                nRet = this.SendCallMsg(bodyDom,
                    libName,
                    bindPatronList,
                    fullPatronName,
                    markFullPatronName,
                    workerList,
                    maskDef,
                    send2PatronIsMask,
                    out strError);
            }
            else if (strType == "借书成功")
            {
                nRet = this.SendBorrowMsg(bodyDom,
                    libName,
                    bindPatronList,
                    patronBarcode,
                    patronName,
                    libraryCode,
                    workerList,
                    maskDef,
                    send2PatronIsMask,
                    out strError);
            }
            else if (strType == "还书成功")
            {
                nRet = this.SendReturnMsg(bodyDom,
                    libName,
                    bindPatronList,
                    fullPatronName,
                    markFullPatronName,
                    workerList,
                    send2PatronIsMask,
                    out strError);
            }
            else if (strType == "交费")
            {
                nRet = this.SendPayMsg(bodyDom,
                    libName,
                    bindPatronList,
                    fullPatronName,
                    markFullPatronName,
                    workerList,
                    send2PatronIsMask,
                    out strError);
            }
            else if (strType == "撤销交费")
            {
                nRet = this.SendCancelPayMsg(bodyDom,
                    libName,
                    bindPatronList,
                    fullPatronName,
                    markFullPatronName,
                    workerList,
                    send2PatronIsMask,
                    out strError);
            }
            else if (strType == "以停代金到期")
            {
                nRet = this.SendYtdjMsg(bodyDom,
                    libName,
                    bindPatronList,
                    fullPatronName,
                    markFullPatronName,
                    workerList,
                    send2PatronIsMask,
                    out strError);
            }
            else if (strType == "预约备书结果")
            {
                nRet = this.SendBeiShuMsg(bodyDom,
                    libName,
                    bindPatronList,
                    fullPatronName,
                    markFullPatronName,
                    workerList,
                    send2PatronIsMask,
                    out strError);
            }
            else
            {
                strError = "不支持的消息类型[" + strType + "]";
                return -1;
            }

            return nRet;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="libraryCode"></param>
        /// <param name="send2PatronIsMask">如果专门配置了给读者发送才发送</param>
        /// <returns></returns>
        public string GetMaskDef(string libId, string libraryCode, out bool send2PatronIsMask)
        {
            send2PatronIsMask = false;
            // 2021/8/3 屏蔽读者信息,配置在libcfg中
            LibModel libCfg = this._areaMgr.GetLibCfg(libId, libraryCode);
            string maskDef = "barcode:1|0,name:1|0";  // 默认的屏蔽信息兼容不配置的情况，原来馆员监控时可选是否mask
            if (libCfg != null && string.IsNullOrEmpty(libCfg.patronMaskValue) == false)
            {
                maskDef = libCfg.patronMaskValue;
                send2PatronIsMask = true;
            }

            return maskDef;
        }

        // 获取简编字段规则
        public string GetFieldsMap(string libId, string libraryCode,out string  biblioDbName)
        {
            /*
ISBN|010$a
题名|200$a
第一作者|200$f
个人主要作者|701$a
             */
            string fieldsMap ="";

            biblioDbName = "";

            LibModel libCfg = this._areaMgr.GetLibCfg(libId, libraryCode);
            if (libCfg != null && string.IsNullOrEmpty(libCfg.fieldsMap) == false)
            {
                fieldsMap = libCfg.fieldsMap;
                biblioDbName = libCfg.biblioDbName;
            }
            return fieldsMap;
        }

        /// <summary>
        /// 发送登录验证码
        /// </summary>
        /// <param name="bodyDom"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        private int SendLoginVerifyCode(LibEntity lib, XmlDocument bodyDom, out string strError)
        {
            strError = "";
            /*
             <root>
                <type>登录验证码</type>
                <phoneNumber>123</phoneNumber>
                <text>登录验证码为9999。十分钟以后失效</text>
            </root>
             */
            XmlNode root = bodyDom.DocumentElement;
            //手机号
            XmlNode phoneNumberNode = root.SelectSingleNode("phoneNumber");
            string phone = DomUtil.GetNodeText(phoneNumberNode);
            //内容
            XmlNode textNode = root.SelectSingleNode("text");
            string strBody = DomUtil.GetNodeText(textNode);

            // 得到xml
            string strXml = "<root><tel>" + phone + "</tel></root>";
            try
            {
                // 发送一条消息
                // parameters:
                //      strPatronBarcode    读者证条码号
                //      strPatronXml    读者记录XML字符串。如果需要除证条码号以外的某些字段来确定消息发送地址，可以从XML记录中取
                //      strMessageText  消息文字
                //      strError    [out]返回错误字符串
                // return:
                //      -1  发送失败
                //      0   没有必要发送
                //      >=1   发送成功，返回实际发送的消息条数
                string strBarcode = "";
                string libraryCode = "";
                if (lib != null)
                    libraryCode = lib.libName;
                MessageInterface external_interface = this.GetMessageInterface("sms");
                int nRet = external_interface.HostObj.SendMessage(
                    strBarcode,
                    strXml,
                    strBody,
                    libraryCode, //todo,注意这里原来传的code 还是读者的libraryCode
                    out strError);
                if (nRet == -1)
                {
                    strError = "发送登录验证码出错：" + strError;
                    return -1;
                }
            }
            catch (Exception ex)
            {
                strError = "发送登录验证码 异常: " + ex.Message;
                return -1;
            }

            // 20170508 发现民族所的校验码没有发送成功，这里特别写一个日志
            WriteDebug("发送登录验证码成功:" + strBody);

            return 0;
        }

        #region 内部函数

        private string _msgFirstLeft = "尊敬的读者：您好，";
        private string _msgRemark = "如有疑问，请联系系统管理员。";


        // 2021/8/3 作废掉，原来的mask非常简单，只留了最后一个字符，改为与dp2ssl相同的配置
        private string markString1(string text)
        {
            if (String.IsNullOrEmpty(text) == true)
                return "";

            return text.Substring(text.Length - 1).PadLeft(text.Length, '*');
        }



        /// <summary>
        /// 发送微信通知
        /// </summary>
        /// <param name="weixinIds">需发送给的绑定帐户对象数组</param>
        /// <param name="templateName">模板名</param>
        /// <param name="msgData">发送内容</param>
        /// <param name="linkUrl">链接地址</param>
        /// <param name="theOperator">
        /// 操作人,这里为什么没有把操作人放在内容里，是因为要与日期拼起来
        /// 而日期是在这里统一加上的
        /// </param>
        /// <param name="strError"></param>
        /// <returns>
        /// -1 出错
        /// 0 成功
        /// </returns>
        private int SendTemplateMsgInternal(List<WxUserItem> userList,
            string templateName,
          object msgData,
          string linkUrl,
          string theOperator,
          string originalXml,
          out string strError)
        {
            strError = "";

            BaseTemplateData templateData = (BaseTemplateData)msgData;
            string oldRemark = templateData.remark.value;

            // 加日期与操作人
            string nowTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");//dp2WeiXinService.DateTimeToStringNoSec(DateTime.Now);
            templateData.remark.value = oldRemark + "\n" + nowTime;
            if (string.IsNullOrEmpty(theOperator)==false)
                templateData.remark.value = templateData.remark.value + " " + theOperator;

            // 给每个帐户发通知
            foreach (WxUserItem u in userList)//string oneWeixinId in weixinIds)
            {
                // 把消息信息写日志和本地库
                string messageXml = ((BaseTemplateData)msgData).Dump();
                // 写到日志里
                {
                    string msgType = templateName;
                    if (u.type == WxUserDatabase.C_Type_Patron)
                        this.WriteDebug("即将给读者发通知：weixin=[" + u.weixinId + "],图书馆为[" + u.libName + "]的读者[" + u.readerName + "(" + u.readerBarcode + ")]" + "发送[" + msgType + "]通知");
                    else
                        this.WriteDebug("即将给工作人员发通知：weixin=[" + u.weixinId + "],图书馆为[" + u.libName + "]的工作人员[" + u.userName + "]" + "发送[" + msgType + "]通知");

                    // 只有微信公众号来源的，才写日志，web和小程序来源的会写到本地库，就不写日志了，节省一点日志空间
                    if (WxUserDatabase.CheckFrom(u.weixinId) == WxUserDatabase.C_from_weixin)//.CheckIsFromWeb(u.weixinId) == false)
                        this.WriteDebug(messageXml);
                }

                try
                {
                    string pureWeixinId = u.weixinId;//oneWeixinId;
                    GzhCfg gzh = null;

                    // 先检查通知发到哪个公众号，即检查weixinid是否包括@，
                    // 有一些用户单位是用自己单位的公众号关联到我爱图书馆，通知要发到用户单位
                    int nIndex = u.weixinId.IndexOf("@");
                    if (nIndex > 0)
                    {
                        pureWeixinId = u.weixinId.Substring(0, nIndex);

                        // 得到对应的公众号信息
                        string gzhAppId = u.weixinId.Substring(nIndex + 1);
                        if (gzhAppId != "")
                        {
                            gzh = this._gzhContainer.GetByAppId(gzhAppId);
                        }
                    }

                    // 如果微信id中没带appid，取默认公众号设置
                    if (gzh == null)
                    {
                        gzh = this._gzhContainer.GetDefault();
                    }

                    // 此时如果为null，则处理下一条
                    if (gzh == null)
                    {
                        this.WriteErrorLog("发送失败：[" + u.weixinId + "]未找到对应的公众号");
                        continue;
                    }

                    // 2020/5/24 尝试放开，把消息写到本地库，方便看详情
                    // 2021/8/13 消息库太大了，暂时不要写了
                    // 2020/4/1 发微信通知前，都写到本地库中
                    UserMessageItem myMsg = new UserMessageItem();
                    myMsg.refid = Guid.NewGuid().ToString();
                    myMsg.userId = u.id;// 2020/4/2 这里应该用userId,这样可以关联到绑定帐户,而不是用单纯的weixinId
                    myMsg.msgType = templateName;
                    myMsg.xml = messageXml;
                    myMsg.originalXml = originalXml;  //长度不限  2023/6/7增加
                    myMsg.createTime = nowTime;//GetNowTime();
                    UserMessageDb.Current.Add(myMsg);

                    // 只有微信公众号入口，才发微信通知
                    if (WxUserDatabase.CheckFrom(u.weixinId) == WxUserDatabase.C_from_weixin) //.CheckIsFromWeb(u.weixinId) == false) //weixinId.Length > 2 && weixinId.Substring(0, 2) == "~~")
                    {
                        // 注意：这里一定要用一个临时变量，否则修改后会影响其它的消息，整个就混乱了。
                        string myLinkUrl = linkUrl;
                        //this.WriteDebug(nIndex1.ToString() + " linkUrl==[" + myLinkUrl + "]");

                        // 给linkUrl加上msgRefid，用于进入消息界面，定位到对应消息上
                        if (myLinkUrl.IndexOf("[message]") != -1)
                        {
                            myLinkUrl = myLinkUrl.Replace("[message]", "");
                            myLinkUrl = this.GetOAuth2Url_func(myLinkUrl, HttpUtility.UrlEncode("Library/Message?msgRefid=" + myMsg.refid));
                            // 2023/6/8 写日志
                            //this.WriteDebug("oauth[" + myLinkUrl + "]");
                        }

                        // 调微信接口发送消息
                        string appId = gzh.appId;
                        string templateId = gzh.GetTemplateId(templateName);
                        WriteDebug("开始获取accessToken");

                        var accessToken = AccessTokenContainer.GetAccessToken(appId);
                        WriteDebug("完成获取accessToken=[" + accessToken + "]");

                        var ret = TemplateApi.SendTemplateMessage(accessToken,
                            pureWeixinId,  // 用单纯的weixinId
                            templateId,
                            myLinkUrl,
                            msgData);

                        WriteDebug("SendTemplateMessage完成,errorcode=[" + ret.errcode + "],errorstr=[" + ret.errmsg + "]");
                        if (ret.errcode != 0)
                        {
                            // 出错，写日志，继续下一条
                            this.WriteErrorLog("发送失败：" + ret.errmsg);
                            continue;
                        }
                    }
                }
                catch (Exception ex0)
                {
                    this.WriteErrorLog("走进异常");

                    // 2018/3/18 4:46:29 ERROR:给[o4xvUvpencKbMoW2wPVe1mswD9O4@wx57aa3682c59d16c2]发送CaoQi微信通知异常:微信Post请求发生错误！错误代码：43004，说明：require subscribe hint: [UJ5fwa0589ge21]
                    // 遇到43004的问题，表示用户未关注公众号。那么将这些帐户删除。
                    if (ex0.Message.IndexOf("43004") != -1)
                    {
                        strError = "走进异常,发送失败:" + u.weixinId + "已取消关注。\r\n" + ex0.Message;
                        this.WriteErrorLog(strError);

                        // 2020/8/3先注册掉，以后再处理，优先级不高
                        //string tempInfo = "";
                        //List<WxUserItem> uList = WxUserDatabase.Current.Get(u.weixinId, "", -1);
                        //foreach (WxUserItem one in uList)
                        //{
                        //    if (tempInfo != "")
                        //        tempInfo += ",";

                        //    if (one.type == WxUserDatabase.C_Type_Patron)
                        //        tempInfo += u.libName + "-" + u.readerName + "[" + u.readerBarcode + "]";
                        //    else
                        //        tempInfo += u.libName + "-" + u.userName;
                        //    //WxUserDatabase.Current.SimpleDelete(user.id); //2020/4/2先不要轻易删除
                        //}

                        //this.WriteDebug("发通知时发现微信号" + u.weixinId + "已取消关注公众号,之前绑定的帐户有:" + tempInfo);
                    }
                    else
                    {
                        strError = "走进异常,发送失败:" + ex0.Message;
                        this.WriteErrorLog(strError);
                    }
                }


            }

            // 还回原来的值，因为是引用型，外面还在用这个data
            ((BaseTemplateData)msgData).remark.value = oldRemark;

            return 0;
        }


        /// <summary>
        /// 发给微信模板消息统一入口
        /// </summary>
        /// <param name="templateName">模板名称</param>
        /// <param name="weixinIds">微信用户id数组</param>
        /// <param name="workers">工作人员数组</param>
        /// <param name="msgData">显文消息内容</param>
        /// <param name="markMsgData">mask消息内容</param>
        /// <param name="linkUrl">链接</param>
        /// <param name="theOperator">操作人</param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int SendTemplateMsg1(string templateName,
            List<WxUserItem> patrons, //List<string> weixinIds,
            List<WxUserItem> workers,
            object msgData,
            object maskMsgData,
            string linkUrl,
            string theOperator,
            bool send2PatronIsMask,
            string originalXml,
            out string strError)
        {
            int nRet = 0;
            strError = "";

            // 发给本人
            if (patrons.Count > 0)
            {
                // 2021/8/3 判断发给读者的信息是否Mask
                object msg = msgData;
                if (send2PatronIsMask == true)
                    msg = maskMsgData;

                nRet = this.SendTemplateMsgInternal(patrons,
                    templateName,
                    msg,//msgData,
                    linkUrl,
                    "",// theOperator,
                    originalXml,
                    out strError);
                if (nRet == -1)
                    return -1;
            }

            // 发给工作人员
            if (workers.Count > 0)
            {
                this.WriteDebug("给下面" + workers.Count + "个工作人员发监控消息");

                // 将工作人员分成两组，一组是发全文内容，一组是发mask内容
                List<WxUserItem> noMaskWorkers = null;
                List<WxUserItem> maskWorkers = null;
                SplitWorkers(workers, out noMaskWorkers, out maskWorkers);
                if (noMaskWorkers.Count > 0)
                {
                    nRet = this.SendTemplateMsgInternal(noMaskWorkers,
                        templateName,
                        msgData,
                        linkUrl,
                        theOperator,
                        originalXml,
                        out strError);
                    if (nRet == -1)
                        return -1;
                }

                if (maskWorkers.Count > 0)
                {
                    nRet = this.SendTemplateMsgInternal(maskWorkers,
                        templateName,
                        maskMsgData,
                        linkUrl,
                        theOperator,
                        originalXml,
                        out strError);
                    if (nRet == -1)
                        return -1;
                }
            }
            return 0;
        }

        // 将工作人员分成两类，一类是不隐藏敏感信息的，一类是隐藏敏感信息的
        // 是否mask是每个工作人员自己在页面开关上设置的
        private void SplitWorkers(List<WxUserItem> users,
            out List<WxUserItem> noMaskWorkers,  //out List<string> workerIds,
            out List<WxUserItem> maskWorkers)
        {

            noMaskWorkers = new List<WxUserItem>();
            maskWorkers = new List<WxUserItem>();

            // 将这边tracing on的工作人员分为2组，一组是mask的，一组是不mask的
            foreach (WxUserItem user in users)
            {
                string fullWeixinId = user.weixinId;//2016-11-16 weixinId带了@appId // + "@" + user.AppId;

                // 是否mask是每个工作人员自己在页面开关上设置的
                if (user.IsMask == false)
                    noMaskWorkers.Add(user);
                else
                    maskWorkers.Add(user);
            }
        }

        /// <summary>
        /// 获取读者记录中绑定的微信id,返回数组
        /// </summary>
        /// <param name="bodyDom"></param>
        /// <param name="patronName"></param>
        /// <returns></returns>
        private List<WxUserItem> GetBindWeiXinIds(string libId,
            XmlDocument bodyDom,
            out string patronBarcode,
            out string patronName,
            out string libraryCode)
        {
            patronBarcode = "";
            patronName = "";
            libraryCode = "";

            XmlNode root = bodyDom.DocumentElement;
            XmlNode patronRecordNode = root.SelectSingleNode("patronRecord");
            if (patronRecordNode == null)
                throw new Exception("尚未定义<patronRecordNode>节点");

            // barcode
            XmlNode node = patronRecordNode.SelectSingleNode("barcode");
            if (node != null)
                patronBarcode = DomUtil.GetNodeText(node);

            // name
            node = patronRecordNode.SelectSingleNode("name");
            if (node != null)
                patronName = DomUtil.GetNodeText(node);

            // libraryCode
            node = patronRecordNode.SelectSingleNode("libraryCode");
            if (node != null)
                libraryCode = DomUtil.GetNodeText(node);

            // 取微信id
            //List<string> weixinIdList = new List<string>();
            List<WxUserItem> userList = WxUserDatabase.Current.Get("", libId, null, WxUserDatabase.C_Type_Patron,
                 patronBarcode,
                 "",
                 true);

            return userList;

            //foreach (WxUserItem user in userList)
            //{
            //    // 不是weixin的会写到一个本地消息库中。
            //    //// 如果是web过来的，不处理
            //    //if (WxUserDatabase.CheckIsFromWeb(user.weixinId) == true)
            //    //    continue;

            //    weixinIdList.Add(user.weixinId);
            //}


            //return weixinIdList;
        }


        // 获得打开监控功能的数字平台管理员 和 本图书馆管理员
        private List<WxUserItem> GetTraceUsers(string libId,
            string libraryCode)
        {
            List<WxUserItem> traceUsers = new List<WxUserItem>();

            // 从内存中查找
            foreach (WxUserItem tUser in this.TracingOnUserList)
            {
                // 要写到
                //// web入口的weixin不用发通知
                //if (WxUserDatabase.CheckIsFromWeb(tUser.weixinId) == true)//tUser.weixinId.Length > 2 && tUser.weixinId.Substring(0, 2) == WxUserDatabase.C_Prefix_fromWeb) // "~~")
                //    continue;

                // 数字平台工作人员
                if (tUser.IsdpAdmin == true)
                {
                    traceUsers.Add(tUser);
                    continue;
                }

                if (tUser.libId == libId && tUser.libraryCode == libraryCode)
                {
                    traceUsers.Add(tUser);
                    continue;
                }

            }

            return traceUsers;
        }

        private string GetFullPatronName(string patronName,
            string patronCode,
            string libName,
            string libraryCode,
            bool bMark,
            string maskDef)
        {
            string info = "";
            if (string.IsNullOrEmpty(patronName) == false)
            {
                if (bMark == false)
                    info += patronName;
                else
                    info += dp2WeiXinService.Mask(maskDef, patronName, "name");//this.markString(patronName);
            }

            if (string.IsNullOrEmpty(patronCode) == false)
            {
                if (info != "")
                    info += " ";

                if (bMark == false)
                    info += patronCode;
                else
                    info += dp2WeiXinService.Mask(maskDef, patronCode, "barcode");//this.markString(patronCode);
            }

            string fullLibName = this.GetFullLibName(libName, libraryCode, "");
            if (String.IsNullOrEmpty(fullLibName) == false)
            {
                info += "(" + fullLibName + ")";
            }

            return info;
        }

        private string GetFullLibName(string libName, string libraryCode, string location)
        {
            string fullLibName = libName;

            if (String.IsNullOrEmpty(libraryCode) == false)
            {
                if (fullLibName != "")
                    fullLibName += "/";

                fullLibName += libraryCode;
            }

            if (String.IsNullOrEmpty(location) == false)
            {
                if (fullLibName != "")
                    fullLibName += "/";

                fullLibName += location;
            }

            return fullLibName;
        }

        private string GetFullItemBarcode(string itemBarcode, string libName, string location)
        {
            string info = itemBarcode;

            string fullLibName = this.GetFullLibName(libName, "", location);
            if (fullLibName != "")
                info += "(" + fullLibName + ")";

            return info;
        }

        public string GetShortSummary(string summary)
        {
            int nIndex = summary.IndexOf("/");
            if (nIndex > 0)
            {
                summary = summary.Substring(0, nIndex);
            }
            else
            {
                //西文标点，点、空、横、横、空
                nIndex = summary.IndexOf(". -- ");
                if (nIndex > 0)
                {
                    summary = summary.Substring(0, nIndex);
                }
            }
            return summary;
        }

        #endregion

        //SendBeiShuMsg
        /// <returns>
        /// 发送备书结果通知
        /// </returns>
        private int SendBeiShuMsg(XmlDocument bodyDom,
            string libName,
            List<WxUserItem> bindPatronList,
            string fullPatronName,
            string maskFullPatronName,
            List<WxUserItem> workers,
            bool send2PatronIsMask,
            out string strError)
        {
            strError = "";

            //WriteDebug("1.走到SendBeiShuMsg()");

            XmlNode root = bodyDom.DocumentElement;
            string strContent = DomUtil.GetElementText(root, "content");

            //WriteDebug("2."+ strContent);


            string operTime = DateTime.Now.ToString("yyyy/MM/dd");


            // 备注
            string remark = this._msgRemark;

            string first_text = "☀☀☀☀☀☀☀☀☀☀";
            string first_color = "#9400D3";
            string title = "预约备书结果";
            //{{first.DATA}}
            //标题：{{keyword1.DATA}}
            //时间：{{keyword2.DATA}}
            //内容：{{keyword3.DATA}}
            //{{remark.DATA}}
            MessageTemplateData msgData = new MessageTemplateData(first_text,
                first_color,
                title,
                operTime,
                strContent,
                remark);

            // mask
            //strText = strText.Replace(fullPatronName, maskFullPatronName);
            MessageTemplateData maskMsgData = new MessageTemplateData(first_text,
                first_color,
                title,
                operTime,
                strContent,
                remark);

            int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_Message,
                bindPatronList,//bindWeixinIds,
                workers,
                msgData,
                maskMsgData,
                this._messageLinkUrl,//linkurl
                "",//theOperator,
                send2PatronIsMask,  // 2021/8/3 不mask
                bodyDom.OuterXml,
                out strError);
            if (nRet == -1)
                return -1;

            //WriteDebug("3.发送备书消息完成");


            return 0;
        }

        /// <returns>
        /// 处理以停代金消息
        /// </returns>
        private int SendYtdjMsg(XmlDocument bodyDom,
            string libName,
            List<WxUserItem> bindPatronList,
            string fullPatronName,
            string maskFullPatronName,
            List<WxUserItem> workers,
            bool send2PatronIsMask,
            out string strError)
        {
            strError = "";
            /*
<root>
    <type>以停代金到期</type>
…

根元素下的items元素下，有一个或者多个overdue元素，记载了刚到期的以停代金事项信息。
在patronRecord的下级元素overdues下，可以看到若干overdue元素，这是当前还未到期或者交费的事项。只要还有这样的事项，读者就不被允许借书，只能还书。所以通知消息文字组织的时候，可以考虑提醒尚余多少违约事项，这样可以避免读者空高兴一场以为马上可以借书了
     
           */

            XmlNode root = bodyDom.DocumentElement;

            // 操作时间
            XmlNode nodeOperTime = root.SelectSingleNode("operTime");
            if (nodeOperTime == null)
            {
                strError = "尚未定义<borrowDate>节点";
                return -1;
            }
            string operTime = DomUtil.GetNodeText(nodeOperTime);
            operTime = DateTimeUtil.ToLocalTime(operTime, "yyyy/MM/dd");

            // 多笔以停代金事项
            XmlNodeList listOverdue = root.SelectNodes("items/overdue");
            string barcodes = "";
            double totalPrice = 0;

            string lastLocation = "";
            foreach (XmlNode node in listOverdue)
            {
                string oneBarcode = DomUtil.GetAttr(node, "barcode");

                string tempLibName = libName;

                string location = DomUtil.GetAttr(node, "location");
                if (location != lastLocation)
                {
                    // 更新lastLocation
                    lastLocation = location;
                }
                else
                {
                    // location与上一item的location相同，则不见显示
                    tempLibName = "";
                    location = "";
                }

                string fullItemBarcode = this.GetFullItemBarcode(oneBarcode, tempLibName, location);

                if (barcodes != "")
                    barcodes += ",";
                barcodes += fullItemBarcode;

                string price = DomUtil.GetAttr(node, "price");
                if (String.IsNullOrEmpty(price) == false && price.Length > 3)
                {
                    double dPrice = Convert.ToDouble(price.Substring(3));
                    totalPrice += dPrice;
                }
            }
            string strText = fullPatronName + "，您有册条码为 " + barcodes + " 项违约以停代金到期了，";

            // 还存在违约项未到期的情况
            XmlNodeList listOverdue1 = root.SelectNodes("patronRecord/overdues/overdue");
            if (listOverdue1.Count > 0)
            {
                strText += "您还有" + listOverdue1.Count.ToString() + "项违约未到期，还不能借书。";
            }
            else
            {
                strText += "您可以继续借书了。";
            }

            // 备注
            string remark = "\n" + this._msgRemark;

            string first_text = "☀☀☀☀☀☀☀☀☀☀";
            string first_color = "#9400D3";
            string title = "以停代金到期";
            //{{first.DATA}}
            //标题：{{keyword1.DATA}}
            //时间：{{keyword2.DATA}}
            //内容：{{keyword3.DATA}}
            //{{remark.DATA}}
            MessageTemplateData msgData = new MessageTemplateData(first_text,
                first_color,
                title,
                operTime,
                strText,
                remark);

            // mask
            strText = strText.Replace(fullPatronName, maskFullPatronName);
            MessageTemplateData maskMsgData = new MessageTemplateData(first_text,
                first_color,
                title,
                operTime,
                strText,
                remark);

            int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_Message,
                bindPatronList,//bindWeixinIds,
                workers,
                msgData,
                maskMsgData,
                this._messageLinkUrl,//linkurl
                "",//theOperator,
                send2PatronIsMask,
                bodyDom.OuterXml,
                out strError);
            if (nRet == -1)
                return -1;

            return 0;
        }

        // 20220622 召回通知
        /*
<?xml version="1.0" encoding="utf-8"?>
<root>
    <type>召回通知</type>
    <reason>毕业手续需要</reason>
    <items overdueCount="0" normalCount="3">
        <item barcode="B003" location="流通库" refID="" summary="唐宋词人年谱 [专著]  / 夏承焘著. -- ISBN 978-7-5540-0713-6 (精装 ) : CNY60.00" borrowDate="Fri, 09 Jun 2023 11:09:12 +0800" borrowPeriod="31day" timeReturning="2023/7/10" overdue="" overdueType="warning" />
        <item barcode="B002" location="流通库" refID="" summary="杜诗详注 [专著]  / (唐)杜甫撰 ; (清)仇兆鳌注. -- ISBN 978-7-101-10528-5 (精装 ) : CNY156.00" borrowDate="Thu, 08 Jun 2023 15:37:08 +0800" borrowPeriod="31day" timeReturning="2023/7/9" overdue="已超期 1 天" overdueType="overdue" />
        <item barcode="B001" location="流通库" refID="" summary="文心雕龙义证 [专著]  / (南朝梁)刘勰著 ; 詹锳义证. -- ISBN 7-5325-0326-7 : CNY150.00" borrowDate="Thu, 08 Jun 2023 14:04:18 +0800" borrowPeriod="31day" timeReturning="2023/7/9" overdue="已超期 1 天" overdueType="overdue" />
    </items>
    <text>因毕业手续需要，图书馆提醒您尽快归还下列书刊：
唐宋词人年谱 [专著]  / 夏承焘著. -- ISBN 978-7-5540-0713-6 (精装 ) : CNY60.00 
杜诗详注 [专著]  / (唐)杜甫撰 ; (清)仇兆鳌注. -- ISBN 978-7-101-10528-5 (精装 ) : CNY156.00 
文心雕龙义证 [专著]  / (南朝梁)刘勰著 ; 詹锳义证. -- ISBN 7-5325-0326-7 : CNY150.00 
</text>
    <patronRecord>
        <barcode>P001</barcode>
        <readerType>本科生</readerType>
        <name>test</name>
        <refID>768bac1c-c149-487a-ae47-d7fe3ae3ff1b</refID>
        <libraryCode>
        </libraryCode>
        <borrows>
            <borrow barcode="B003" oi="" recPath="中文图书实体/3" biblioRecPath="中文图书/3" location="流通库" borrowDate="Fri, 09 Jun 2023 11:09:12 +0800" borrowPeriod="31day" borrowID="45a40514-42a6-4625-b5ec-c5be3c6da4de" returningDate="Mon, 10 Jul 2023 12:00:00 +0800" operator="supervisor" type="普通" price="CNY60.00" notifyHistory="" />
            <borrow barcode="B002" oi="" recPath="中文图书实体/2" biblioRecPath="中文图书/2" location="流通库" borrowDate="Thu, 08 Jun 2023 15:37:08 +0800" borrowPeriod="31day" borrowID="de62e17f-2a04-434e-bf02-b06744f44988" returningDate="Sun, 09 Jul 2023 12:00:00 +0800" operator="supervisor" type="普通" price="CNY156.00" notifyHistory="" />
            <borrow barcode="B001" oi="" recPath="中文图书实体/1" biblioRecPath="中文图书/1" location="流通库" borrowDate="Thu, 08 Jun 2023 14:04:18 +0800" borrowPeriod="31day" borrowID="b73954c9-cbf2-4ae4-becb-5b9b55b77272" returningDate="Sun, 09 Jul 2023 12:00:00 +0800" operator="supervisor" type="普通" price="CNY150.00" notifyHistory="" />
        </borrows>
        <email>weixinid:~~d65388ef-5ffd-47d6-b4e9-4efc070d4fd6,weixinid:~~13aaa56e-2100-4741-9323-0d0bc6913b7c</email>
        <overdues>
        </overdues>
    </patronRecord>
</root>
         */
        private int SendCallMsg(XmlDocument bodyDom,
             string libName,
             List<WxUserItem> bindPatronList,
                                 //string patronBarcode,
                                 //string patronName,
                                 //string patronLibraryCode,
             string fullPatronName,
             string maskFullPatronName,
             List<WxUserItem> workers,
             string maskDef, //2021/8/3 mask定义
             bool send2PatronIsMask,
             out string strError)
        {
            strError = "";
            XmlNode root = bodyDom.DocumentElement;

            // 直接获取text
            string text = DomUtil.GetElementText(root, "text");
            /*
<text>因 毕业手续需要，图书馆提醒您尽快归还下列书刊：
彼得兔的故事 [专著]  / (英)比阿特丽斯·波特著 ; (美)查尔斯·桑托利绘 ; 司南译. -- ISBN 978-7-5346-5588-3 : CNY16.80 
兔子布莱尔 [专著]  / (美)约耳·钱德勒·哈里斯原著 ; (美)唐·戴利绘 ; 司南译. -- ISBN 978-7-5346-5595-1 : CNY16.80 
</text>
             */


            /* 这是老方式，将召开通知整个作为一个消息发出，内容为text
            // 备注
            string remark = this._msgRemark;
            string operTime = DateTime.Now.ToString("yyyy/MM/dd");

            string first_text = "☀☀☀☀☀☀☀☀☀☀";
            string first_color = "#9400D3";
            string title = "图书召回通知";
            //{{first.DATA}}
            //标题：{{keyword1.DATA}}
            //时间：{{keyword2.DATA}}
            //内容：{{keyword3.DATA}}
            //{{remark.DATA}}
            MessageTemplateData msgData = new MessageTemplateData(first_text,
                first_color,
                title,
                operTime,
                text,
                remark);

            // mask
            //strText = strText.Replace(fullPatronName, maskFullPatronName);
            MessageTemplateData maskMsgData = new MessageTemplateData(first_text,
                first_color,
                title,
                operTime,
                remark,
                remark);

            int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_Message,
                bindPatronList,//bindWeixinIds,
                workers,
                msgData,
                maskMsgData,
                this._messageLinkUrl,//linkurl
                "",//theOperator,
                send2PatronIsMask,  // 2021/8/3 不mask
                bodyDom.OuterXml,
                out strError);
            if (nRet == -1)
                return -1;

            */

            string first_text = "☀☀☀☀☀☀☀☀☀☀";
            string first_color = "#9400D3";

            // <reason>毕业手续需要</reason>
            string reason = DomUtil.GetElementText(root, "reason");

            //string remark = this._msgRemark;

            // 2023/6/9 改为将召回消息根据里面的册，发出多条消息
            /*
    <reason>毕业手续需要</reason>
    <items overdueCount="0" normalCount="3">
        <item barcode="B003" location="流通库" refID="" summary="唐宋词人年谱 [专著]  / 夏承焘著. -- ISBN 978-7-5540-0713-6 (精装 ) : CNY60.00" borrowDate="Fri, 09 Jun 2023 11:09:12 +0800" borrowPeriod="31day" timeReturning="2023/7/10" overdue="" overdueType="warning" />
        <item barcode="B002" location="流通库" refID="" summary="杜诗详注 [专著]  / (唐)杜甫撰 ; (清)仇兆鳌注. -- ISBN 978-7-101-10528-5 (精装 ) : CNY156.00" borrowDate="Thu, 08 Jun 2023 15:37:08 +0800" borrowPeriod="31day" timeReturning="2023/7/9" overdue="已超期 1 天" overdueType="overdue" />
        <item barcode="B001" location="流通库" refID="" 
            summary="文心雕龙义证 [专著]  / (南朝梁)刘勰著 ; 詹锳义证. -- ISBN 7-5325-0326-7 : CNY150.00" 
            borrowDate="Thu, 08 Jun 2023 14:04:18 +0800" 
            borrowPeriod="31day" timeReturning="2023/7/9" 
            overdue="已超期 1 天" overdueType="overdue" />
    </items>
             */
            //// 备注，检查是否有超期信息
            //remark = fullPatronName + "，"+this._msgRemark;//"，感谢及时归还，欢迎继续借书。";
            //XmlNodeList listOverdue = root.SelectNodes("patronRecord/overdues/overdue");
            //if (listOverdue.Count > 0)
            //{
            //    remark = fullPatronName + "，您有" + listOverdue.Count + "笔超期违约记录，请履行超期手续。";
            //}
            XmlNodeList list = root.SelectNodes("items/item");
            foreach (XmlNode item in list)
            {
                /*
{{first.DATA}}
书刊摘要：{{keyword1.DATA}}
册条码号：{{keyword2.DATA}}
借书日期：{{keyword3.DATA}}
应还日期：{{keyword4.DATA}}
召回原因：{{keyword5.DATA}}
{{remark.DATA}}
*/
                string summary = DomUtil.GetAttr(item, "summary");
                string barcode = DomUtil.GetAttr(item, "barcode");

                string borrowDate = DomUtil.GetAttr(item, "borrowDate");
                borrowDate = DateTimeUtil.ToLocalTime(borrowDate, "yyyy/MM/dd");

                string timeReturning = DomUtil.GetAttr(item, "timeReturning");

                string overdueType = DomUtil.GetAttr(item, "overdueType");
                string overdue = DomUtil.GetAttr(item, "overdue");

                string strText = fullPatronName ;
                if (string.IsNullOrEmpty(overdue) == false)
                    strText += "，" + "该书" + overdue + "。";
                else
                    strText += "，";
                string remark = strText + this._msgRemark;


                RecallTemplateData msgData = new RecallTemplateData(first_text,
                    first_color,
                    summary,
                    barcode,
                    borrowDate,
                    timeReturning,
                    reason,
                    remark);

               
                // 脱敏信息
                remark=remark.Replace(fullPatronName, maskFullPatronName); 
                RecallTemplateData maskMsgData = new RecallTemplateData(first_text,
                     first_color,
                     summary,
                     barcode,
                     borrowDate,
                     timeReturning,
                     reason,
                     remark);



                int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_Recall,
                    bindPatronList,//bindWeixinIds,
                    workers,
                    msgData,
                    maskMsgData,
                    this._messageLinkUrl,//linkurl
                    "",//theOperator,
                    send2PatronIsMask,  // 2021/8/3 不mask
                    bodyDom.OuterXml,
                    out strError);
                if (nRet == -1)
                    return -1;
            }


            return 0;
        }

        /// 借书成功
        /// <returns>
        /// -1 出错，
        /// 0 成功
        /// </returns>
        private int SendBorrowMsg(XmlDocument bodyDom,
            string libName,
            List<WxUserItem> bindPatronList,
            string patronBarcode,
            string patronName,
            string patronLibraryCode,
            List<WxUserItem> workers,
            string maskDef, //2021/8/3 mask定义
            bool send2PatronIsMask,
            out string strError)
        {
            strError = "";

            /*
 <root>
  <type>借书成功</type>
  <libraryCode></libraryCode>
  <operation>borrow</operation>
  <action>borrow</action>
  <readerBarcode>R0000001</readerBarcode>
  <itemBarcode>0000001</itemBarcode>
  <borrowDate>Sun, 22 May 2016 19:48:01 +0800</borrowDate>
  <borrowPeriod>31day</borrowPeriod>
  <returningDate>Wed, 22 Jun 2016 12:00:00 +0800</returningDate>
  <price>$4.65</price>
  <no>0</no>
  <bookType>普通</bookType>
  <operator>supervisor</operator>
  <operTime>Sun, 22 May 2016 19:48:01 +0800</operTime>
  <clientAddress>::1</clientAddress>
  <version>1.02</version>
  <uid>062724d8-80c1-4752-979a-b1cd548466be</uid>
  <patronRecord>
    <barcode>R0000001</barcode>
    <readerType>本科生</readerType>
    <name>张三</name>
    <borrows>
      <borrow barcode="0000001" recPath="中文编目实体/1" biblioRecPath="中文编目/602" borrowDate="Sun, 22 May 2016 19:48:01 +0800" borrowPeriod="31day" returningDate="Wed, 22 Jun 2016 12:00:00 +0800" operator="supervisor" type="普通" price="$4.65" />
    </borrows>
    <refID>be13ecc5-6a9c-4400-9453-a072c50cede1</refID>
    <department>/</department>
    <address>address</address>
    <cardNumber>C12345</cardNumber>
    <refid>8aa41a9a-fb42-48c0-b9b9-9d6656dbeb76</refid>
    <email>email:xietao@dp2003.com,weixinid:testwx2,testid:123456</email>
    <idCardNumber>1234567890123</idCardNumber>
    <tel>13641016400</tel>
    <libraryCode></libraryCode>
  </patronRecord>
  <itemRecord>
    <parent>602</parent>
    <refID>59b613c6-fe09-4280-8884-43f2b045c41c</refID>
    <barcode>0000001</barcode>
    <location>流通库</location>
    <price>$4.65</price>
    <bookType>普通</bookType>
    <accessNo>U664.121/N590</accessNo>
    <borrower>R0000001</borrower>
    <borrowerReaderType>本科生</borrowerReaderType>
    <borrowerRecPath>读者/1</borrowerRecPath>
    <borrowDate>Sun, 22 May 2016 19:48:01 +0800</borrowDate>
    <borrowPeriod>31day</borrowPeriod>
    <returningDate>Wed, 22 Jun 2016 12:00:00 +0800</returningDate>
    <operator>supervisor</operator>
    <summary>船舶柴油机 / 聂云超主编. -- ISBN 7-81007-115-7 : $4.65</summary>
  </itemRecord>
</root>            
           */
            XmlNode root = bodyDom.DocumentElement;
            //<itemBarcode>0000001</itemBarcode>
            //<borrowDate>Sun, 22 May 2016 19:48:01 +0800</borrowDate>
            //<borrowPeriod>31day</borrowPeriod>
            //<returningDate>Wed, 22 Jun 2016 12:00:00 +0800</returningDate>

            string volume = "";
            XmlNode nodeVolume = root.SelectSingleNode("volume");
            if (nodeVolume == null)
                nodeVolume = root.SelectSingleNode("itemRecord/volume");
            if (nodeVolume != null)
            {
                volume = DomUtil.GetNodeText(nodeVolume);
            }

            // 册条码
            XmlNode nodeItemBarcode = root.SelectSingleNode("itemBarcode");
            if (nodeItemBarcode == null)
            {
                strError = "未定义<itemBarcode>节点";
                return -1;
            }
            string itemBarcode = DomUtil.GetNodeText(nodeItemBarcode);

            //期限
            XmlNode nodeBorrowPeriod = root.SelectSingleNode("borrowPeriod");
            if (nodeBorrowPeriod == null)
            {
                strError = "未定义<borrowPeriod>节点";
                return -1;
            }
            string borrowPeriod = DomUtil.GetNodeText(nodeBorrowPeriod);
            borrowPeriod = GetDisplayTimePeriodStringEx(borrowPeriod);

            // 借书日期
            XmlNode nodeBorrowDate = root.SelectSingleNode("borrowDate");
            if (nodeBorrowDate == null)
            {
                strError = "未定义<borrowDate>节点";
                return -1;
            }
            string borrowDate = DomUtil.GetNodeText(nodeBorrowDate);
            borrowDate = DateTimeUtil.ToLocalTime(borrowDate, "yyyy/MM/dd");
            // 借书日期的值为 日期+期限 2016-9-2
            borrowDate = borrowDate + " 期限为" + borrowPeriod;

            //应还日期
            XmlNode nodeReturningDate = root.SelectSingleNode("returningDate");
            if (nodeReturningDate == null)
            {
                strError = "未定义<returningDate>节点";
                return -1;
            }
            string returningDate = DomUtil.GetNodeText(nodeReturningDate);
            returningDate = DateTimeUtil.ToLocalTime(returningDate, "yyyy/MM/dd");

            // 摘要
            XmlNode nodeSummary = root.SelectSingleNode("itemRecord/summary");
            if (nodeSummary == null)
            {
                strError = "未定义itemRecord/summary节点";
                return -1;
            }
            string summary = DomUtil.GetNodeText(nodeSummary);

            // 馆藏地
            string location = "";
            XmlNode nodeLocation = root.SelectSingleNode("itemRecord/location");
            if (nodeLocation != null)
                location = DomUtil.GetNodeText(nodeLocation);

            // 册条码完整表示 C001 图书馆/馆藏地
            string fullItemBarcode = this.GetFullItemBarcode(itemBarcode, libName, location);

            // 操作人 operator
            string theOperator = "";
            XmlNode nodeOperator = root.SelectSingleNode("operator");
            if (nodeOperator != null)
            {
                theOperator = DomUtil.GetNodeText(nodeOperator);
            }

            // 备注
            string remark = patronName + "，祝您阅读愉快。";//，欢迎再借。";

            // 完整证条码 
            string fullPatronBarcode = this.GetFullPatronName("", patronBarcode, libName, patronLibraryCode,
                false,
                maskDef);

            //2023/5/15 加 姓名
            fullPatronBarcode = patronName+ " " + fullPatronBarcode;

            summary = this.GetShortSummary(summary);

            //增加卷册信息
            if (volume != "")
                summary += "(" + volume + ")";

            //您好，您已借书成功。 腾讯工作人员您好，虽然模板库中已存在类似模板，但与我司的字段定义不同，我司为几千家图书馆提供专业服务，需要采用专业术语（例如书刊摘要，册条码号，证条码号等）,以免被行内人士吐槽，请批准，谢谢！
            //书刊摘要：中国机读目录格式使用手册 / 北京图书馆《中国机读目录格式使用手册》编委会. -- ISBN 7-80039-990-7 : ￥58.00
            //册条码号：C0000001
            //借书日期：2016-07-01 期限为31天
            //应还日期：2016-07-31
            //证条码号：R0000001
            //xxx，祝您阅读愉快，欢迎再借。
            string first = "▉▊▋▍▎▉▊▋▍▎▉▊▋▍▎";
            string first_color = "#006400";
            BorrowTemplateData msgData = new BorrowTemplateData(first,
                first_color,
                summary,
                fullItemBarcode,
                borrowDate,
                returningDate,
                fullPatronBarcode,
                remark);

            //mask
            //证条码号处
            string tempFullPatronBarcode = this.GetFullPatronName("", patronBarcode, libName, patronLibraryCode,
                true,
                maskDef);

            //备注姓名
            string markPatronName = dp2WeiXinService.Mask(maskDef, patronName, "name"); //this.markString(patronName);

            // 2023/5/17 增加姓名
            tempFullPatronBarcode =  markPatronName + " " + tempFullPatronBarcode;

            string tempRemark = remark.Replace(patronName, markPatronName);// +theOperator; ;


            BorrowTemplateData maskMsgData = new BorrowTemplateData(first,
                first_color,
                summary,
                fullItemBarcode,
                borrowDate,
                returningDate,
                tempFullPatronBarcode,
                tempRemark);

            int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_Borrow,
                bindPatronList,//bindWeixinIds,
                workers,
                msgData,
                maskMsgData,
                this._messageLinkUrl,//linkurl
                theOperator,
                send2PatronIsMask,
                bodyDom.OuterXml,
                out strError);
            if (nRet == -1)
                return -1;

            return 0;
        }


        /// 还书成功
        /// <returns>
        /// -1 出错，格式出错或者发送模板消息出错
        /// 0 成功
        /// </returns>
        private int SendReturnMsg(XmlDocument bodyDom,
            string libName,
            List<WxUserItem> bindPatronList,
            string fullPatronName,
            string markFullPatronName,
            List<WxUserItem> workers,
            bool send2PatronIsMask,
            out string strError)
        {
            strError = "";
            /*
<type>还书成功</type>
<libraryCode>分馆1</libraryCode>
<operation>return</operation>
<action>return</action>
<borrowDate>Sun, 04 Sep 2016 15:22:02 +0800</borrowDate>
<borrowPeriod>31day</borrowPeriod>
<returningDate>Wed, 05 Oct 2016 12:00:00 +0800</returningDate>
<borrowOperator>jane</borrowOperator>
<itemBarcode>C1000</itemBarcode>
<readerBarcode>L1-001</readerBarcode>
<operator>jane</operator>
<operTime>Mon, 05 Sep 2016 01:58:21 +0800</operTime>
<clientAddress>::1</clientAddress>
<version>1.02</version>
<time></time>
<uid>7ae41585-cc60-47cf-b3f0-708ea77b5a8e</uid>
<patronRecord>
  <barcode>L1-001</barcode>
  <refID>84dbbde6-7aac-4912-9a16-18cfffef9697</refID>
  <readerType>本科</readerType>
  <createDate>Sun, 04 Sep 2016 00:00:00 +0800</createDate>
  <expireDate>Sun, 31 Dec 2017 00:00:00 +0800</expireDate>
  <name>延1</name>
  <hire expireDate="" period="" />
  <borrows></borrows>
  <email>weixinid:o4xvUviTxj2HbRqbQb9W2nMl4fGg</email>
  <libraryCode>分馆1</libraryCode>
</patronRecord>
<itemRecord>
  <barcode>C1000</barcode>
  <parent>3</parent>
  <refID>e4e4d608-0fd8-4fe9-ae66-52b0e9c6a796</refID>
  <state></state>
  <publishTime></publishTime>
  <location>分馆1/阅览室</location>
  <seller></seller>
  <source></source>
  <price></price>
  <bindingCost></bindingCost>
  <bookType>普通</bookType>
  <registerNo></registerNo>
  <comment></comment>
  <mergeComment></mergeComment>
  <batchNo></batchNo>
  <volume></volume>
  <accessNo></accessNo>
  <intact></intact>
  <binding />
  <operations>
    <operation name="create" time="Sun, 04 Sep 2016 14:05:45 +0800" operator="jane" />
  </operations>
  <summary>当我想睡的时候 [专著]  / (美)简·R. 霍华德文 ; (美)琳内·彻丽图 ; 林芳萍翻译. -- ISBN 978-7-5434-7754-4 (精装 ) : CNY27.80</summary>
</itemRecord>      
           */

            XmlNode root = bodyDom.DocumentElement;
            //<itemBarcode>0000001</itemBarcode>
            //<borrowDate>Sun, 22 May 2016 19:48:01 +0800</borrowDate>
            //<borrowPeriod>31day</borrowPeriod>
            //<returningDate>Wed, 22 Jun 2016 12:00:00 +0800</returningDate>

            // 卷册信息
            string volume = "";
            XmlNode nodeVolume = root.SelectSingleNode("volume");
            if (nodeVolume == null)
                nodeVolume = root.SelectSingleNode("itemRecord/volume");
            if (nodeVolume != null)
            {
                volume = DomUtil.GetNodeText(nodeVolume);
            }

            // 册条码
            XmlNode nodeItemBarcode = root.SelectSingleNode("itemBarcode");
            if (nodeItemBarcode == null)
            {
                strError = "尚未定义<itemBarcode>节点";
                return -1;
            }
            string itemBarcode = DomUtil.GetNodeText(nodeItemBarcode);

            // 借书日期
            string borrowDate = "";
            XmlNode nodeBorrowDate = root.SelectSingleNode("borrowDate");
            if (nodeBorrowDate != null)
            {
                borrowDate = DomUtil.GetNodeText(nodeBorrowDate);
                borrowDate = DateTimeUtil.ToLocalTime(borrowDate, "yyyy/MM/dd");
            }

            // 期限
            string borrowPeriod = "";
            XmlNode nodeBorrowPeriod = root.SelectSingleNode("borrowPeriod");
            if (nodeBorrowPeriod != null)
            {
                borrowPeriod = DomUtil.GetNodeText(nodeBorrowPeriod);
                borrowPeriod = GetDisplayTimePeriodStringEx(borrowPeriod);
            }

            // 实际还书日期
            XmlNode nodeOperTime = root.SelectSingleNode("operTime");
            if (nodeOperTime == null)
            {
                strError = "尚未定义<operTime>节点";
                return -1;
            }
            string operTime = DomUtil.GetNodeText(nodeOperTime);
            operTime = DateTimeUtil.ToLocalTime(operTime, "yyyy/MM/dd");

            // 2023/5/15 把证条码和姓名加到 最后一个字段 还书时间。
            operTime += " " + fullPatronName;

            // 摘要
            XmlNode nodeSummary = root.SelectSingleNode("itemRecord/summary");
            if (nodeSummary == null)
            {
                strError = "尚未定义itemRecord/summary节点";
                return -1;
            }
            string summary = DomUtil.GetNodeText(nodeSummary);

            // 馆藏地
            string location = "";
            XmlNode nodeLocation = root.SelectSingleNode("itemRecord/location");
            if (nodeLocation != null)
                location = DomUtil.GetNodeText(nodeLocation);

            // 操作人 operator
            string theOperator = "";
            XmlNode nodeOperator = root.SelectSingleNode("operator");
            if (nodeOperator != null)
            {
                theOperator = DomUtil.GetNodeText(nodeOperator);
                //if (String.IsNullOrEmpty(theOperator) == false)
                //    theOperator = " 操作者：" + theOperator;
            }

            // 册条码完整表示 C001 图书馆/馆藏地
            string fullItemBarcode = this.GetFullItemBarcode(itemBarcode, libName, location);



            // 备注，检查是否有超期信息
            string remark = fullPatronName + "，感谢还书。";//"，感谢及时归还，欢迎继续借书。";
            XmlNodeList listOverdue = root.SelectNodes("patronRecord/overdues/overdue");
            if (listOverdue.Count > 0)
            {
                remark = fullPatronName + "，您有" + listOverdue.Count + "笔超期违约记录，请履行超期手续。";
            }

            //尊敬的读者，您已成功还书。
            //书刊摘要：中国机读目录格式使用手册 / 北京图书馆《中国机读目录格式使用手册》编委会. -- ISBN 7-80039-990-7 : ￥58.00 
            //册条码号：C0000001
            //借书日期：2016-5-27
            //借阅期限：31天
            //还书日期：2016-6-27
            //谢谢您及时归还，欢迎再借。
            summary = this.GetShortSummary(summary);

            //增加卷册信息
            if (volume != "")
                summary += "(" + volume + ")";

            // 
            string first = "▉▊▋▍▎▉▊▋▍▎▉▊▋▍▎";
            string first_color = "#00008B";
            ReturnTemplateData msgData = new ReturnTemplateData(first,
                first_color,
                summary,
                fullItemBarcode,
                borrowDate,
                borrowPeriod,
                operTime,
                remark);


            remark = remark.Replace(fullPatronName, markFullPatronName);

            string maskOperTime=operTime.Replace(fullPatronName, markFullPatronName);
            ReturnTemplateData maskMsgData = new ReturnTemplateData(first,
                first_color,
                summary,
                fullItemBarcode,
                borrowDate,
                borrowPeriod,
                maskOperTime,
                remark);

            // 发送消息
            int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_Return,
                bindPatronList,//bindWeixinIds,
                workers,
                msgData,
                maskMsgData,
                this._messageLinkUrl,//linkurl
                theOperator,
                send2PatronIsMask,
                bodyDom.OuterXml,
                out strError);
            if (nRet == -1)
                return -1;

            return 0;
        }

        /// 交费成功
        /// <returns>
        /// -1 出错，格式出错或者发送模板消息出错
        /// 0 未绑定微信id
        /// 1 成功
        /// </returns>
        private int SendPayMsg(XmlDocument bodyDom,
            string libName,
            List<WxUserItem> bindPatronList,//List<string> bindWeixinIds,
            string fullPatronName,
            string markFullPatronName,
            List<WxUserItem> workers,
            bool send2PatronIsMask,
            out string strError)
        {
            strError = "";
            /*
 <?xml version="1.0" encoding="utf-8"?>
<root>
    <type>交费</type>
    <libraryCode></libraryCode>
    <operation>amerce</operation>
    <action>amerce</action>
    <readerBarcode>R0000001</readerBarcode>
    <operator>supervisor</operator>
    <operTime>Sun, 22 May 2016 19:28:52 +0800</operTime>
    <clientAddress>::1</clientAddress>
    <version>1.02</version>
    <patronRecord>
        <barcode>R0000001</barcode>
        <readerType>本科生</readerType>
        <name>张三</name>
        <refID>be13ecc5-6a9c-4400-9453-a072c50cede1</refID>
        <department>/</department>
        <address>address</address>
        <cardNumber>C12345</cardNumber>
        <refid>8aa41a9a-fb42-48c0-b9b9-9d6656dbeb76</refid>
        <email>email:xietao@dp2003.com,weixinid:testwx2,testid:123456</email>
        <idCardNumber>1234567890123</idCardNumber>
        <tel>13641016400</tel>
        <libraryCode></libraryCode>
    </patronRecord>
    <items>
        <overdue barcode="0000001" summary=”…” reason="超期。超 335天; 违约金因子: CNY1.0/day" overduePeriod="335day" price="CNY335" borrowDate="Tue, 01 Dec 2015 14:09:33 +0800" borrowPeriod="31day" returnDate="Thu, 01 Dec 2016 14:09:52 +0800" borrowOperator="supervisor" operator="supervisor" id="635845758236835562-1" />
    </items>
</root>       
           */
            XmlNode root = bodyDom.DocumentElement;

            //交费时间
            XmlNode nodeOperTime = root.SelectSingleNode("operTime");
            if (nodeOperTime == null)
            {
                strError = "尚未定义<borrowDate>节点";
                return -1;
            }
            string operTime = DomUtil.GetNodeText(nodeOperTime);
            operTime = DateTimeUtil.ToLocalTime(operTime, "yyyy/MM/dd");

            // 2023/5/15 把读者信息 合到交费时间里。
            operTime += " " + fullPatronName;

            // 操作人 operator
            string theOperator = "";
            XmlNode nodeOperator = root.SelectSingleNode("operator");
            if (nodeOperator != null)
            {
                theOperator = DomUtil.GetNodeText(nodeOperator);
                //if (String.IsNullOrEmpty(theOperator) == false)
                //    theOperator = " 操作者：" + theOperator;
            }

            // 备注
            string remark = "\n" + fullPatronName + "，您已成功交费。";// +this._msgRemark;

            XmlNodeList listOverdue = root.SelectNodes("items/overdue");
            foreach (XmlNode node in listOverdue)
            {
                string oneBarcode = DomUtil.GetAttr(node, "barcode");
                string price = DomUtil.GetAttr(node, "price");
                string summary = DomUtil.GetAttr(node, "summary");
                string reason = DomUtil.GetAttr(node, "reason");

                // todo,检查结构中是否有location
                string location = DomUtil.GetAttr(node, "location");
                string fullItemBarcode = this.GetFullItemBarcode(oneBarcode, libName, location);


                summary = this.GetShortSummary(summary);
                //尊敬的读者，您已成功交费。
                //书刊摘要：中国机读目录格式使用手册 / 北京图书馆《中国机读目录格式使用手册》编委会. -- ISBN 7-80039-990-7 : ￥58.00
                //册条码号：C0000001
                //交费金额：CNY 10元
                //交费原因：超期。超1天，违约金因子：CNY1.0/Day
                //交费时间：2015-12-27 13:15
                //如有疑问，请联系系统管理员。
                string first = "💰💰💰💰💰💰💰💰💰💰";
                string first_color = "#556B2F";
                PayTemplateData msgData = new PayTemplateData(first,
                    first_color,
                    summary,
                    fullItemBarcode,
                    price,
                    reason,
                    operTime,
                    remark);

                //mask
                remark = remark.Replace(fullPatronName, markFullPatronName);
                PayTemplateData maskMsgData = new PayTemplateData(first,
                    first_color,
                    summary,
                    fullItemBarcode,
                    price,
                    reason,
                    operTime,
                    remark);

                // 发送消息
                int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_Pay,
                    bindPatronList,//bindWeixinIds,
                    workers,
                    msgData,
                    maskMsgData,
                    this._messageLinkUrl,//linkurl
                    theOperator,
                    send2PatronIsMask,
                    bodyDom.OuterXml,
                    out strError);
                if (nRet == -1)
                    return -1;

            }

            return 0;
        }

        /// 撤消交费成功
        /// <returns>
        /// -1 出错，格式出错或者发送模板消息出错
        /// 0 未绑定微信id
        /// 1 成功
        /// </returns>
        private int SendCancelPayMsg(XmlDocument bodyDom,
            string libName,
            List<WxUserItem> bindPatronList,//List<string> bindWeixinIds,
            string fullPatronName,
            string markFullPatronName,
            List<WxUserItem> workers,
            bool send2PatronIsMask,
            out string strError)
        {
            strError = "";
            /*
 <?xml version="1.0" encoding="utf-8"?>
<root>
    <type>撤销交费</type>
    <libraryCode></libraryCode>
    <operation>amerce</operation>
    <action>undo</action>
    <readerBarcode>R0000001</readerBarcode>
    <operator>supervisor</operator>
    <operTime>Sun, 22 May 2016 19:15:54 +0800</operTime>
    <clientAddress>::1</clientAddress>
    <version>1.02</version>
    <patronRecord>
        <barcode>R0000001</barcode>
        <readerType>本科生</readerType>
        <name>张三</name>
        <refID>be13ecc5-6a9c-4400-9453-a072c50cede1</refID>
        <department>/</department>
        <address>address</address>
        <cardNumber>C12345</cardNumber>
        <overdues>
            <overdue barcode="0000001" reason="超期。超 335天; 违约金因子: CNY1.0/day" overduePeriod="335day" price="CNY335" borrowDate="Tue, 01 Dec 2015 14:09:33 +0800" borrowPeriod="31day" returnDate="Thu, 01 Dec 2016 14:09:52 +0800" borrowOperator="supervisor" operator="supervisor" id="635845758236835562-1" />
        </overdues>
        <refid>8aa41a9a-fb42-48c0-b9b9-9d6656dbeb76</refid>
        <email>email:xietao@dp2003.com,weixinid:testwx2,testid:123456</email>
        <idCardNumber>1234567890123</idCardNumber>
        <tel>13641016400</tel>
        <libraryCode></libraryCode>
    </patronRecord>
    <items>
        <overdue barcode="0000001" summary=”…” reason="超期。超 335天; 违约金因子: CNY1.0/day" overduePeriod="335day" price="CNY335" borrowDate="Tue, 01 Dec 2015 14:09:33 +0800" borrowPeriod="31day" returnDate="Thu, 01 Dec 2016 14:09:52 +0800" borrowOperator="supervisor" operator="supervisor" id="635845758236835562-1" />
    </items>
</root>     
           */
            XmlNode root = bodyDom.DocumentElement;

            //撤消时间
            XmlNode nodeOperTime = root.SelectSingleNode("operTime");
            if (nodeOperTime == null)
            {
                strError = "尚未定义<borrowDate>节点";
                return -1;
            }
            string operTime = DomUtil.GetNodeText(nodeOperTime);
            operTime = DateTimeUtil.ToLocalTime(operTime, "yyyy/MM/dd");

            // 2023/5/15 把读者信息 合到撤消交费时间里。
            operTime += " " + fullPatronName;

            // 操作人 operator
            string theOperator = "";
            XmlNode nodeOperator = root.SelectSingleNode("operator");
            if (nodeOperator != null)
            {
                theOperator = DomUtil.GetNodeText(nodeOperator);
                //if (String.IsNullOrEmpty(theOperator) == false)
                //    theOperator = " 操作者：" + theOperator;
            }

            // 备注
            string remark = "\n" + fullPatronName + "，您已成功撤消交费。";// +this._msgRemark;

            XmlNodeList listOverdue = root.SelectNodes("items/overdue");
            foreach (XmlNode node in listOverdue)
            {
                string oneBarcode = DomUtil.GetAttr(node, "barcode");
                string price = DomUtil.GetAttr(node, "price");
                string summary = DomUtil.GetAttr(node, "summary");
                string reason = DomUtil.GetAttr(node, "reason");

                // todo,检查结构中是否有location
                string location = DomUtil.GetAttr(node, "location");
                string fullItemBarcode = this.GetFullItemBarcode(oneBarcode, libName, location);

                summary = this.GetShortSummary(summary);
                //{{first.DATA}}
                //书刊摘要：{{keyword1.DATA}}
                //册条码号：{{keyword2.DATA}}
                //交费原因：{{keyword3.DATA}}
                //撤消金额：{{keyword4.DATA}}
                //撤消时间：{{keyword5.DATA}}
                //{{remark.DATA}}
                string first = "✈ ☁ ☁ ☁ ☁ ☁ ☁";
                string first_color = "#B8860B";
                CancelPayTemplateData msgData = new CancelPayTemplateData(first,
                    first_color,
                    summary,
                    fullItemBarcode,
                    reason,
                    price,
                    operTime,
                    remark);
                //mask
                remark = remark.Replace(fullPatronName, markFullPatronName);
                CancelPayTemplateData maskMsgData = new CancelPayTemplateData(first,
                    first_color,
                    summary,
                    fullItemBarcode,
                    reason,
                    price,
                    operTime,
                    remark);

                // 发送消息
                int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_CancelPay,
                    bindPatronList,//bindWeixinIds,
                    workers,
                    msgData,
                    maskMsgData,
                    this._messageLinkUrl,//linkurl
                    theOperator,
                    send2PatronIsMask,
                    bodyDom.OuterXml,
                    out strError);
                if (nRet == -1)
                    return -1;
            }
            return 0;
        }


        /// <summary>
        /// 超期通知
        /// </summary>
        /// <param name="bodyDom"></param>
        private int SendCaoQi(XmlDocument bodyDom,
            string libName,
            List<WxUserItem> bindPatronList,//List<string> bindWeixinIds,
            string fullPatronName,
            string markFullPatronName,
            List<WxUserItem> workers,
            bool send2PatronIsMask,
            out string strError)
        {
            strError = "";
            /*
<root>
    <type>超期通知</type>
    <items overdueCount="1" normalCount="0">
        <item barcode="" refID="" 
             * summary="船舶柴油机 / 聂云超主编. -- ISBN 7-..." 
             * timeReturning="2016/5/18" 
             * overdue="已超期 31 天" 
             * overdueType="overdue" />
    </items>
    <text>您借阅的下列书刊：
船舶柴油机 / 聂云超主编. -- ISBN 7-... 应还日期: 2016/5/18 已超期 31 天
</text>
    <patronRecord>...
    </patronRecord>
</root>
            
//overdueType是超期类型，overdue表示超期，warning表示即将超期。             
           */
            XmlNode root = bodyDom.DocumentElement;

            // 取出册列表
            XmlNode nodeItems = root.SelectSingleNode("items");
            string overdueCount = DomUtil.GetAttr(nodeItems, "overdueCount");

            XmlNodeList nodeList = nodeItems.SelectNodes("item");
            // 一册一个通知
            foreach (XmlNode item in nodeList)
            {
                string summary = DomUtil.GetAttr(item, "summary");
                string timeReturning = DomUtil.GetAttr(item, "timeReturning");
                string overdue = DomUtil.GetAttr(item, "overdue");

                string barcode = DomUtil.GetAttr(item, "barcode");

                // 馆藏地 todo
                string location = DomUtil.GetAttr(item, "location");
                string fullItemBarcode = this.GetFullItemBarcode(barcode, libName, location);

                // 借书日期
                string borrowDate = DomUtil.GetAttr(item, "borrowDate");
                borrowDate = DateTimeUtil.ToLocalTime(borrowDate, "yyyy/MM/dd");

                //overdueType是超期类型，overdue表示超期，warning表示即将超期。
                string overdueType = DomUtil.GetAttr(item, "overdueType");


                string remark = "";
                if (overdueType == "overdue")
                {
                    remark = "\n" + fullPatronName + "，您借出的图书已超期，请尽快归还。";
                }
                else if (overdueType == "warning")
                {
                    remark = "\n" + fullPatronName + "，您借出的图书即将到期，请注意不要超期，留意归还。您可在公众号'我的图书馆/我的信息/在借'界面办理续借。";
                }
                else
                {
                    strError = "overdueType属性值[" + overdueType + "]不合法。";
                    return -1;//整个不处理 //continue;                    
                }

                summary = this.GetShortSummary(summary);
                //您好，您借出的图书已超期。
                //书刊摘要：中国机读目录格式使用手册 / 北京图书馆《中国机读目录格式使用手册》编委会. -- ISBN 7-80039-990-7 : ￥58.00
                //册条码号：C0000001
                //借书日期：2016-07-01
                //应还日期：2016-07-31
                //超期情况：已超期30天
                //任延华，您借出的图书已超期，请尽快归还。

                if (overdueType == "overdue")
                {
                    remark = "\n" + fullPatronName + "，您借出的图书已超期，请尽快归还。";
                    string first = "📙📙📙📙📙📙📙📙📙📙";
                    string first_color = "#FFFF00";
                    CaoQiTemplateData msgData = new CaoQiTemplateData(first,
                        first_color,
                        summary,
                        fullItemBarcode,
                        borrowDate,
                        timeReturning,
                        overdue,
                        remark);

                    //mask
                    remark = remark.Replace(fullPatronName, markFullPatronName);
                    CaoQiTemplateData maskMsgData = new CaoQiTemplateData(first,
                        first_color,
                        summary,
                        fullItemBarcode,
                        borrowDate,
                        timeReturning,
                        overdue,
                        remark);

                    // 发送消息
                    int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_CaoQi,
                        bindPatronList,//bindWeixinIds,
                        workers,
                        msgData,
                        maskMsgData,
                        this._messageLinkUrl,//linkurl
                        "",//theOperator,
                        send2PatronIsMask,
                        bodyDom.OuterXml,
                        out strError);
                    if (nRet == -1)
                        return -1;

                }
                else if (overdueType == "warning")
                {
                    remark = "\n" + fullPatronName + "，您借出的图书即将到期，请注意不要超期，留意归还。您可在公众号'我的图书馆/我的信息/在借'界面办理续借。";
                    string first = "📙📙📙📙📙📙📙📙📙📙";
                    string first_color = "#FFFF00";
                    KuaiCaoQiTemplateData msgData = new KuaiCaoQiTemplateData(first,
                        first_color,
                        summary,
                        fullItemBarcode,
                        borrowDate,
                        timeReturning,
                        overdue,
                        remark);

                    //mask
                    remark = remark.Replace(fullPatronName, markFullPatronName);
                    KuaiCaoQiTemplateData maskMsgData = new KuaiCaoQiTemplateData(first,
                        first_color,
                        summary,
                        fullItemBarcode,
                        borrowDate,
                        timeReturning,
                        overdue,
                        remark);

                    // 发送消息
                    int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_KuaiCaoQi,
                        bindPatronList,//bindWeixinIds,
                        workers,
                        msgData,
                        maskMsgData,
                        this._messageLinkUrl,//linkurl
                        "",//theOperator,
                        send2PatronIsMask,
                        bodyDom.OuterXml,
                        out strError);
                    if (nRet == -1)
                        return -1;
                }
            }
            return 0;
        }

        /// <summary>
        /// 预约到书通知
        /// </summary>
        /// <param name="bodyDom"></param>
        /// <param name="strError"></param>
        /// <returns>
        /// -1 出错，格式出错或者发送模板消息出错
        /// 0 未绑定微信id
        /// 1 成功
        /// </returns>
        private int SendArrived(XmlDocument bodyDom,
            LibEntity lib,
            List<WxUserItem> bindPatronList,//List<string> bindWeixinIds,
            string fullPatronName,
            string markFullPatronName,
            List<WxUserItem> workers,
            bool send2PatronIsMask,
            out string strError)
        {
            strError = "";
            string libName = "";
            if (lib != null)
                libName = "";


            /*
           body元素里面是预约到书通知记录(注意这是一个字符串，需要另行装入一个XmlDocument解析)，其格式如下：
           <?xml version="1.0" encoding="utf-8"?>
 <root>
    <type>预约到书通知</type>
            <source>dp2mini</source>
    <itemBarcode>0000001</itemBarcode>
    <refID> </refID>
    <onShelf>false</onShelf>
    <opacURL>/book.aspx?barcode=0000001</opacURL>
    <reserveTime>2天</reserveTime>
    <today>2016/5/17 10:10:59</today>
    <summary>船舶柴油机 / 聂云超主编. -- ISBN 7-...</summary>
    <patronName>张三</patronName>
    <patronRecord>
        <barcode>R0000001</barcode>
        <readerType>本科生</readerType>
        <name>张三</name>
        <refID>be13ecc5-6a9c-4400-9453-a072c50cede1</refID>
        <department>数学系</department>
        <address>address</address>
        <cardNumber>C12345</cardNumber>
        <refid>8aa41a9a-fb42-48c0-b9b9-9d6656dbeb76</refid>
        <email>email:xietao@dp2003.com,weixinid:testwx2</email>
        <tel>13641016400</tel>
        <idCardNumber>1234567890123</idCardNumber>
    </patronRecord>
</root>
           */


            XmlNode root = bodyDom.DocumentElement;
            //<onShelf>false</onShelf>
            // <reserveTime>2天</reserveTime>
            // <today>2016/5/17 10:10:59</today>

            // 是否在架
            XmlNode nodeOnShelf = root.SelectSingleNode("onShelf");
            if (nodeOnShelf == null)
            {
                strError = "尚未定义<onShelf>节点";
                return -1;
            }
            string onShelf = DomUtil.GetNodeText(nodeOnShelf);
            bool bOnShelf = false;
            if (onShelf == "true")
                bOnShelf = true;

            string source = DomUtil.GetElementText(root, "source");

            if (source != "dp2mini") // 2021/3/2加：如果是dp2mini发过来的预约到书，一定要发给读者
            {
                // 如果图书馆配了在架预约不发通知，则不给读者发通知 2020/2/14 编译局使用的馆员备书功能
                //if (bOnShelf == true
                //    && string.IsNullOrEmpty(lib.comment) == false
                //    && lib.comment.IndexOf("OnshelfArrivedNoNotice") != -1)

                // 2024/1/15 修改
                if (lib.IsSendArrivedNotice == null)
                    lib.IsSendArrivedNotice = "Y";

                // //2020/3/22 改为直接使用变量
                // 2023/11/2 改为一个丰富信息的字符串
                if (lib.IsSendArrivedNotice != null && lib.IsSendArrivedNotice.Contains("N") == true) 
                {

                    if (lib.IsSendArrivedNotice == "N")
                    {
                        //return 0;  //2020-3-9改为不给读者立即发，还是给监控的工作人员发送
                        bindPatronList.Clear(); //bindWeixinIds.Clear();
                    }
                    else
                    {

                        // 将不发通知的分馆过滤掉。

                        List<WxUserItem> sendPatrons = new List<WxUserItem>();

                        // 2023/11/2 解析字符串，格式为: 分馆1:N;分馆2:N
                        List<string> fenguanList = SplitArrivedParam(lib.IsSendArrivedNotice);
                        foreach (WxUserItem one in bindPatronList)
                        {
                            // 读者不属于不发消息的分馆，才会给这个读者发通知
                            if (fenguanList.Contains(one.libraryCode) == false)
                            {
                                //bindPatronList.Remove(one);   //这样写会报错
                                sendPatrons.Add(one);
                            }
                        }

                        bindPatronList = sendPatrons;
                    }
                }
            }

            // 摘要
            XmlNode nodeSummary = root.SelectSingleNode("summary");
            if (nodeSummary == null)
            {
                strError = "尚未定义<summary>节点";
                return -1;
            }
            string summary = DomUtil.GetNodeText(nodeSummary);

            //<accessNo>
            XmlNode accessNoToday = root.SelectSingleNode("accessNo");
            if (accessNoToday == null)
            {
                strError = "尚未定义<accessNo>节点";
                return -1;
            }
            string accessNo = DomUtil.GetNodeText(accessNoToday);

            // 到书日期
            XmlNode nodeToday = root.SelectSingleNode("today");
            if (nodeToday == null)
            {
                strError = "尚未定义<today>节点";
                return -1;
            }
            string today = DomUtil.GetNodeText(nodeToday);

            //保留期限
            XmlNode nodeReserveTime = root.SelectSingleNode("reserveTime");
            if (nodeReserveTime == null)
            {
                strError = "尚未定义<reserveTime>节点";
                return -1;
            }
            string reserveTime = DomUtil.GetNodeText(nodeReserveTime);//2天

            // 计算保留至日期
            string toDate = "";
            try
            {
                DateTime tempDate = DateTime.Parse(today);
                if (reserveTime.Contains("天") == true)
                {
                    int day = Convert.ToInt32(reserveTime.Replace("天", ""));
                    if (day > 0)
                    {
                        tempDate = tempDate.AddDays(day);
                        toDate = tempDate.ToShortDateString();
                    }
                }
                else if (reserveTime.Contains("小时") == true)
                {
                    int hour = Convert.ToInt32(reserveTime.Replace("小时", ""));
                    if (hour > 0)
                    {
                        tempDate = tempDate.AddHours(hour);
                        toDate = tempDate.ToShortDateString();
                    }
                }
            }
            catch (Exception ex)
            {
                this.WriteErrorLog("计算保留至的日期出错：" + ex.Message);
            }

            reserveTime = "保留" + reserveTime;

            if (toDate != "")
            {
                reserveTime += "(至" + toDate + ")";
            }



            // 册条码
            string itemBarcode = "";
            XmlNode nodeItemBarcode = root.SelectSingleNode("itemBarcode");
            if (nodeItemBarcode != null)
            {
                itemBarcode = DomUtil.GetNodeText(nodeItemBarcode);
            }
            if (itemBarcode == "")
            {
                XmlNode nodeRefId = root.SelectSingleNode("refID");
                if (nodeRefId != null)
                {
                    itemBarcode = DomUtil.GetNodeText(nodeRefId);
                }
            }

            //reserve
            string requestDate = "";
            XmlNode nodeRequestDate = root.SelectSingleNode("requestDate");
            if (nodeRequestDate != null)
            {
                requestDate = DomUtil.GetNodeText(nodeRequestDate);
                requestDate = DateTimeUtil.ToLocalTime(requestDate, "yyyy/MM/dd");
            }


            // 馆藏地
            string location = "";
            XmlNode nodeLocation = root.SelectSingleNode("location");
            if (nodeLocation != null)
            {
                location = DomUtil.GetNodeText(nodeLocation);
            }
            string fullItemBarcode = this.GetFullItemBarcode(itemBarcode, libName, location);

            // 备注
            string remark = fullPatronName + "，您预约的图书到了，请尽快来图书馆办理借书手续。";// " + fullItemBarcode + " //如果您未能在保留期限内来馆办理借阅手续，图书馆将把优先借阅权转给后面排队等待的预约者，或做归架处理。";
            if (bOnShelf == true)
            {
                remark = fullPatronName + "，您预约的图书已经在架上，请尽快来图书馆办理借书手续。";// " + fullItemBarcode + " //如果您未能在保留期限内来馆办理借阅手续，图书馆将把优先借阅权转给后面排队等待的预约者，或允许其他读者借阅。";
            }
            if (string.IsNullOrEmpty(accessNo) == false)
            {
                remark += "索取号" + accessNo;
            }

            // 2023/5/15  加上姓名
            reserveTime += " " + fullPatronName; 

            summary = this.GetShortSummary(summary);
            //您好，您预约的图书已经到书。
            //书刊摘要：中国机读目录格式使用手册 / 北京图书馆《中国机读目录格式使用手册》编委会. -- ISBN 7-80039-990-7 : ￥58.00
            //册条码号：C00001
            //预约日期：2016-08-15
            //到书日期：2016-09-05
            //保留期限：2016-09-07（保留2天）
            //XXX，您预约的图书到了，请尽快来图书馆办理借书手续，请尽快来图书馆办理借书手续。
            //如果您未能在保留期限内来馆办理借阅手续，图书馆将把优先借阅权转给后面排队等待的预约者，或做归架处理。
            string first = "📗📗📗📗📗📗📗📗📗📗";
            string first_color = "#FF8C00";
            ArrivedTemplateData msgData = new ArrivedTemplateData(first,
                first_color,
                summary,
                fullItemBarcode,
                requestDate,
                today,
                reserveTime,
                remark);
            //mask
            remark = remark.Replace(fullPatronName, markFullPatronName);
            ArrivedTemplateData maskMsgData = new ArrivedTemplateData(first,
                first_color,
                summary,
                fullItemBarcode,
                requestDate,
                today,
                reserveTime,
                remark);

            // 发送消息
            int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_Arrived,
                bindPatronList,
                workers,
                msgData,
                maskMsgData,
                this._messageLinkUrl,//linkurl
                "",//theOperator,
                send2PatronIsMask,
                bodyDom.OuterXml,
                out strError);
            if (nRet == -1)
                return -1;

            return 0;
        }


        public List<string> SplitArrivedParam(string text)
        {
            //lib.IsSendArrivedNotice
            List<string> list = new List<string>();

            string[] array=text.Trim().Split(new char[] { ';' });
            foreach (string one in array)
            {
                string[] temp = one.Split(new char[] { ':' });
                if (temp.Length==2 && temp[1]=="N")
                {
                    list.Add(temp[0]);
                }
            }

            return list;
        }


        #endregion

        #region 向第三方转发通知

        /// <summary>
        /// 向外部接口转送通知
        /// </summary>
        /// <param name="noticeXml"></param>
        /// <param name="assemblyName"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int TransNotice(string noticeXml,
            string assemblyName,
            out string info,
            out string strError)
        {
            strError = "";
            info = "";
            int nRet = 0;
            //string assemblyName = "LmxxReceiveNoticeInterface";
            if (string.IsNullOrEmpty(assemblyName) == true)
            {
                strError = "assemblyName参数不能为空";
                return -1;
            }
            try
            {
                NoticeInterface external_interface = null;
                nRet = GetNoticeInterface(assemblyName,
                    out external_interface,
                    out strError);
                if (nRet == -1)
                {
                    strError = "GetNoticeInterface出错：" + strError;
                    return -1;
                }

                // 外部接口发送通知
                nRet = external_interface.SendNotice(noticeXml,
                    out info,
                    out strError);
                if (nRet == -1)
                {
                    strError = "SendNotice出错：" + strError;
                    return -1;
                }
            }
            catch (Exception ex)
            {
                strError = "调外部接口assemblyName异常: " + ex.Message;
                return -1;
            }

            return 0;
        }

        // DigitalPlatform.Interfaces.NoticeInterface 类的派生类的对象
        public int GetNoticeInterface(string assemblyName,
            out NoticeInterface noticeInterface,
            out string strError)
        {
            strError = "";
            noticeInterface = null;// new MessageInterface();

            if (string.IsNullOrEmpty(assemblyName) == true)
            {
                strError = "assemblyName参数为空";
                return -1;
            }

            Assembly assembly = Assembly.Load(assemblyName);
            if (assembly == null)
            {
                strError = "名字为 '" + assemblyName + "' 的Assembly加载失败...";
                return -1;
            }

            Type hostEntryClassType = ScriptManager.GetDerivedClassType(
                assembly,
                "DigitalPlatform.Interfaces.NoticeInterface");
            if (hostEntryClassType == null)
            {
                strError = "名字为 '" + assemblyName + "' 的Assembly中未找到 DigitalPlatform.Interfaces.NoticeInterface类的派生类，初始化扩展通知接口失败...";
                return -1;
            }

            noticeInterface = (NoticeInterface)hostEntryClassType.InvokeMember(null,
               BindingFlags.DeclaredOnly |
               BindingFlags.Public | BindingFlags.NonPublic |
               BindingFlags.Instance | BindingFlags.CreateInstance, null, null,
               null);
            if (noticeInterface == null)
            {
                strError = "创建 DigitalPlatform.Interfaces.NoticeInterface 类的派生类的对象（构造函数）失败，初始化扩展消息接口失败...";
                return -1;
            }

            return 1;
        }

        #endregion

        #region 短信接口

        public List<MessageInterface> m_externalMessageInterfaces = null;

        // 初始化扩展的消息接口
        /*
    <externalMessageInterface>
        <interface type="sms" assemblyName="chchdxmessageinterface"/>
    </externalMessageInterface>
         */
        // parameters:
        // return:
        //      -1  出错
        //      0   当前没有配置任何扩展消息接口
        //      1   成功初始化
        public int InitialExternalMessageInterfaces(XmlDocument dom, out string strError)
        {
            strError = "";

            this.m_externalMessageInterfaces = null;
            XmlNode root = dom.DocumentElement.SelectSingleNode("externalMessageInterface");
            if (root == null)
            {
                strError = "在weixin.xml中没有找到<externalMessageInterface>元素";
                return 0;
            }

            this.m_externalMessageInterfaces = new List<MessageInterface>();

            XmlNodeList nodes = root.SelectNodes("interface");
            foreach (XmlNode node in nodes)
            {
                string strType = DomUtil.GetAttr(node, "type");
                if (String.IsNullOrEmpty(strType) == true)
                {
                    strError = "<interface>元素未配置type属性值";
                    return -1;
                }

                string strAssemblyName = DomUtil.GetAttr(node, "assemblyName");
                if (String.IsNullOrEmpty(strAssemblyName) == true)
                {
                    strError = "<interface>元素未配置assemblyName属性值";
                    return -1;
                }

                MessageInterface message_interface = new MessageInterface();
                message_interface.Type = strType;
                message_interface.Assembly = Assembly.Load(strAssemblyName);
                if (message_interface.Assembly == null)
                {
                    strError = "名字为 '" + strAssemblyName + "' 的Assembly加载失败...";
                    return -1;
                }

                Type hostEntryClassType = ScriptManager.GetDerivedClassType(
        message_interface.Assembly,
        "DigitalPlatform.Interfaces.ExternalMessageHost");
                if (hostEntryClassType == null)
                {
                    strError = "名字为 '" + strAssemblyName + "' 的Assembly中未找到 DigitalPlatform.Interfaces.ExternalMessageHost类的派生类，初始化扩展消息接口失败...";
                    return -1;
                }

                message_interface.HostObj = (ExternalMessageHost)hostEntryClassType.InvokeMember(null,
        BindingFlags.DeclaredOnly |
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.CreateInstance, null, null,
        null);
                if (message_interface.HostObj == null)
                {
                    strError = "创建 type 为 '" + strType + "' 的 DigitalPlatform.Interfaces.ExternalMessageHost 类的派生类的对象（构造函数）失败，初始化扩展消息接口失败...";
                    return -1;
                }

                message_interface.HostObj.App = this;

                this.m_externalMessageInterfaces.Add(message_interface);
            }

            return 1;
        }

        public MessageInterface GetMessageInterface(string strType)
        {
            // 2012/3/29
            if (this.m_externalMessageInterfaces == null)
                return null;

            foreach (MessageInterface message_interface in this.m_externalMessageInterfaces)
            {
                if (message_interface.Type == strType)
                    return message_interface;
            }

            return null;
        }



        #endregion

        #region 检查不在线图书馆

        public const string C_State_Expire = "到期";


        /// <summary>
        /// 检查图书馆是否在线
        /// </summary>
        /// <param name="libEntity"></param>
        /// <param name="clock"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public bool CheckIsOnline(LibEntity libEntity,
            out string clock,
            out string strError)
        {
            strError = "";
            clock = "";
            bool isOnline = true;

            try
            {
                // 使用代理账号capo 20161024 jane
                LoginInfo loginInfo = new LoginInfo("", false);

                //根据时钟，检查图书馆是否在线
                CancellationToken cancel_token = new CancellationToken();
                string id = Guid.NewGuid().ToString();
                SearchRequest request = new SearchRequest(id,
                    loginInfo,
                    "getSystemParameter",
                    "",
                    "_clock",//"cfgs",//queryWord,
                    "",
                    "",
                    "",
                    "",//"getDataDir",//formatList,//
                    1,
                    0,
                    -1);

                /*
                /*2016/10/16
                光光(2820725526) 20:59:53
                我看你的代码中 GetOfflineLibs() 函数中，有这么一段：
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this.dp2MServerUrl,
                    lib.capoUserName).Result;
                你在获得 Connection 的时候，用的名字字符串是“对方账户名”。这是出于什么考虑呢？
                提醒一下，你检测若干对方 dp2library 连通状态的过程，是个循环。每一次做完再做下一个。
                而且是十分钟一次循环。使用频率不是很高，对对方服务器发起请求不存在互相重叠。
                这样，用一根通道就可以了。也就是说，甚至可以在循环外面 GetConnection() ，
                然后在循环里面集中使用这一根通道。
                那么 GetConnection() 时候要获得的通道的名字，就不必和对方或者本方账号名字挂钩了，用个死名字即可。
                打个比方，你和对方一个屋子里面的人循序讲话，你接通电话以后，他们一个一个顺序过来和你对话即可，
                不必用多个电话机。
                这里屋子的比方，就是 dp2mserver。你把请求到达 dp2mserver 即可，
                它会通过你给 SearchTaskAsync() 的 strRemoteUser 参数分发。你这一侧的通道不必用多条。
                记住，无论你和远端多少 dp2Capo 联系，都是中间通过 dp2mserver 分发的。
                有时候我们在微信公众号这一侧启用多根通道(通过名字参数区分)，是为了并发，或者其他需要。
                并不是意味着微信公众号和 dp2Capo “直连”了。
                 */
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    C_ConnName_CheckOfflineLibs).Result;//lib.capoUserName 2016/10/16 jane 改为一根通道了

                SearchResult result = connection.SearchTaskAsync(
                    libEntity.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.ResultCount == -1)
                {
                    // 不一定非得检查targetNotFound，只要返回-1就认为不在线。
                    // if (result.ErrorCode == "TargetNotFound")

                    isOnline = false;
                    strError = result.ErrorInfo;
                }
                else
                {
                    isOnline = true;
                }
            }
            catch (AggregateException ex)
            {
                strError = "异常：" + MessageConnection.GetExceptionText(ex);
                isOnline = false;

            }
            catch (Exception ex)
            {
                strError = "异常：" + ex.Message;
                isOnline = false;
            }

            // 总是漏掉江西警察学校，这里输出日志看看。
            if (isOnline == false)
                this.WriteDebug("检查 " + libEntity.libName + " dp2libary为离线状态。" + strError);
            //else
            //    this.WriteDebug("检查 " + libEntity.libName + " 为在线状态。");


            return isOnline;
        }


        /// <summary>
        /// 获取挂起的图书馆
        /// <returns></returns>
        public List<Library> GetHangupLibs()
        {
            string strError = "";
            //this.WriteDebug("0-0");


            // 先执行一次检查
            this.LibManager.RedoCheckHangup(null);

            //this.WriteDebug("0-1");

            // 本次挂起图书馆
            List<Library> hangupLibs = new List<Library>();

            //this.WriteDebug("0-2");

            // 先拉出配置的所有图书
            List<Library> libs = dp2WeiXinService.Instance.LibManager.Librarys; //LibDatabase.Current.GetLibs();  // 20161016 jane 这里应该用内存的数据库集合是好了 todo
            foreach (Library lib in libs)
            {
                //this.WriteDebug("0-3");

                // 可以图书馆的备注信息设为notwart，则不进行检查是否在线
                if (lib.Entity.comment != null && lib.Entity.comment.IndexOf("notwarn") != -1) // 任延华测试用的图书馆
                    continue;

                //this.WriteDebug("0-4");

                //20171003 到期的图书馆不再提醒
                if (lib.Entity.state == C_State_Expire)
                    continue;

                //this.WriteDebug("0-5");

                // 如果是挂起状态，加入不在线列表
                if (lib.IsHangup == true)
                {
                    //this.WriteDebug("0-6");
                    hangupLibs.Add(lib);
                    continue;
                }
            }
            //this.WriteDebug("0-7");


            this.WriteDebug("本轮检查有" + hangupLibs.Count + "个离线图书馆");
            return hangupLibs;
        }


        // 不在线图书馆缓存列表,主要用来记录上次发送消息的时间的，
        // 即1个小时内不重新发送通过
        // 不在线图书馆包括两类：
        // 1是由于capo版本低，挂起状态的图书馆
        // 2是capo版本正常，但dp2library不通的图书馆
        public Hashtable _offlineLibs = new Hashtable();

        /// <summary>
        /// 对离线图书馆，给该图书馆的管理员 和数字平台管理员发通知
        /// </summary>
        public void WarnOfflineLib()
        {
            string strError = "";
            try
            {
                //this.WriteDebug("1");

                // 本次不在线的图书馆
                List<Library> thisHangupLibs = this.GetHangupLibs();

                //this.WriteDebug("2");

                // 整理缓存中不在线的图书馆
                if (thisHangupLibs.Count == 0)
                {
                    // 清除内存中保存的不在线图书馆
                    if (this._offlineLibs.Count > 0)
                    {
                        this.WriteDebug("清除内存中原先的" + this._offlineLibs.Count + "个离线图书馆");
                        this._offlineLibs.Clear();
                    }
                }
                else
                {
                    //this.WriteDebug("3");

                    // 已经恢复在线的图书馆，要从内存不在线图书馆中删除 
                    List<string> removeIds = new List<string>();
                    foreach (string libid in this._offlineLibs.Keys)
                    {
                        bool bFound = false;
                        foreach (Library lib in thisHangupLibs)
                        {
                            if (libid == lib.Entity.id)
                            {
                                bFound = true;
                                break;
                            }
                        }
                        // 在本次的离线图书馆中不出现，则删除内存中的
                        if (bFound == false)
                        {
                            removeIds.Add(libid);
                        }
                    }

                    //this.WriteDebug("4");

                    // 移除已经连线的图书馆
                    foreach (string oneId in removeIds)
                    {
                        this.WriteDebug("图书馆 " + this.LibManager.GetLibrary(oneId).Entity.libName + " 恢复在线，从内存离线图书馆列表移除。");
                        this._offlineLibs.Remove(oneId);
                    }
                    //this.WriteDebug("5");
                }

                // 处理本次的离线图书馆，得到需要发通知的图书馆
                List<Library> warningLibs = new List<Library>();
                if (thisHangupLibs.Count > 0)
                {
                    //this.WriteDebug("6");

                    DateTime now = DateTime.Now;
                    TimeSpan delta = new TimeSpan(1, 0, 0);
                    foreach (Library lib in thisHangupLibs)
                    {
                        //this.WriteDebug("7");

                        if (this._offlineLibs.ContainsKey(lib.Entity.id) == false)
                        {
                            this.WriteDebug("离线图书馆 " + lib.Entity.libName + " 之前不在内存列表中，加到发通知列表");
                            warningLibs.Add(lib);
                        }
                        else
                        {
                            // 检查上次发通知时间,超过一小时，继续通知
                            DateTime lastTime = (DateTime)this._offlineLibs[lib.Entity.id];
                            if (now - lastTime > delta) //2016/9/25 jane 改bug 比较的2个时间写反了 lastTime-now
                            {
                                warningLibs.Add(lib);
                                this.WriteDebug("离线图书馆 " + lib.Entity.libName + " 之前在内存列表中，距离上次发送时间超过1小时，需再次发通知，加到发通知列表中。");
                            }
                            else
                            {
                                this.WriteDebug("离线图书馆 " + lib.Entity.libName + " 这前在内存列表中，距离上次发送时间小于1小时，此轮不发通知。");

                            }
                        }
                    }

                    //this.WriteDebug("8");
                }

                //===给图书馆发通知=====

                // 数据平台工作人员 weixinid
                List<WxUserItem> dp2003Workers = new List<WxUserItem>();
                if (warningLibs.Count > 0) //有需要发的图书馆通知才查找绑定的数字平台工作人员
                {
                    dp2003Workers = this.GetDp2003WorkerWeixinIds();
                    this.WriteDebug("找到 " + dp2003Workers.Count.ToString() + " 位数字平台工作人员");
                }

                // this.WriteDebug("9");

                foreach (Library lib in warningLibs)
                {
                    this.WriteDebug("准备发送 " + lib.Entity.libName + " 离线通知");

                    // 查找绑定了这个图书馆的工作人员 weixinid
                    List<WxUserItem> libWorkers = this.getWarningWorkerWeixinIds(lib.Entity);

                    this.WriteDebug("找到 " + libWorkers.Count.ToString() + " 位图书馆 " + lib.Entity.libName + " 工作人员");

                    // 当这个图书馆和数字平台都没有可收警告的工作人员，则不再发送通知
                    if (libWorkers.Count == 0 && dp2003Workers.Count == 0)
                    {
                        this.WriteDebug(lib.Entity.libName + "图书馆和数字平台均没有可接收到工作人员");
                        continue;
                    }

                    string libName = lib.Entity.libName;

                    //{{first.DATA}}
                    //标题：{{keyword1.DATA}}
                    //时间：{{keyword2.DATA}}
                    //内容：{{keyword3.DATA}}
                    //{{remark.DATA}}
                    //图书馆桥接服务器失去连接
                    //2016-9-8 15:04
                    //***，贵图书馆 XXX 桥接服务器失去连接，请及时修复，以免影响读者访问。
                    string title = libName + " 桥接服务器dp2capo失去连接";

                    string operTime = DateTimeUtil.DateTimeToString(DateTime.Now);

                    // 此时不需要再次重新检查是否挂起了，只需要得到提示就可以
                    string text = LibraryManager.GetLibHungWarn(lib, false);


                    string first = "☀☀☀☀☀☀☀☀☀☀";
                    string first_color = "#9400D3";
                    MessageTemplateData msgData = new MessageTemplateData(first,
                        first_color,
                        title,
                        operTime,
                        text,
                        "");

                    // 给图书馆工作人员发不在线通知
                    foreach (WxUserItem worker in libWorkers)
                    {
                        if (worker.weixinId == C_Supervisor)
                            continue;

                        string tempText = worker.userName + "，贵馆 " + text;
                        msgData.keyword3 = new TemplateDataItem(tempText, "#000000");

                        //List<string> ids = new List<string>();
                        //string fullWeixinId = worker.weixinId;//2016-11-16 weixinId带了@appId //+ "@" + worker.appId;
                        //ids.Add(fullWeixinId);

                        // 2020-4-2 改为传入对象参数，因为在发送消息的函数里，要写日志和本地库,所以日志需要多一些
                        List<WxUserItem> tempWorkers = new List<WxUserItem>();
                        tempWorkers.Add(worker);
                        int nRet = this.SendTemplateMsgInternal(tempWorkers,//ids,
                            GzhCfg.C_Template_Message,//this.Template_Message,
                             msgData,
                             "",
                             "",// theOperator,
                             "",// originalXml  2023/6/7
                             out strError);
                        if (nRet == -1)
                        {
                            this.WriteErrorLog("给工作人员 " + worker.userName + " 发送图书馆不在线通知出错：" + strError);
                            continue;  //goto ERROR1;                            
                        }

                        this.WriteDebug("给图书馆工作人员 " + worker.userName + " 发送图书馆 " + libName + " 不在线通知完成");

                    }

                    // 给数字平台工作人员发通知
                    foreach (WxUserItem worker in dp2003Workers)
                    {
                        msgData.keyword3 = new TemplateDataItem(text, "#000000");

                        //List<string> ids = new List<string>();
                        //string fullWeixinId = worker.weixinId;//2016-11-16 weixinId带了@appId // + "@" + worker.appId;
                        //ids.Add(fullWeixinId);

                        // 2020-4-2 改为传参数
                        List<WxUserItem> tempWorkers = new List<WxUserItem>();
                        tempWorkers.Add(worker);

                        int nRet = this.SendTemplateMsgInternal(tempWorkers,
                            GzhCfg.C_Template_Message,//this.Template_Message,
                             msgData,
                             "",
                             "",// theOperator,
                             "",// originalXml  2023/6/7
                             out strError);
                        if (nRet == -1)
                        {
                            WriteErrorLog(strError);
                            continue;  //goto ERROR1;                            
                        }

                        this.WriteDebug("给数字平台工作人员 " + worker.userName + " 发送图书馆 " + libName + " 不在线通知完成");
                    }

                    // 加到内存中
                    this._offlineLibs[lib.Entity.id] = DateTime.Now;
                    this.WriteDebug("记下图书馆 " + libName + " 最后发送通知时间" + DateTimeUtil.DateTimeToString(((DateTime)this._offlineLibs[lib.Entity.id])));
                }


                return;

            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }

        ERROR1:
            this.WriteErrorLog(strError);
        }




        /// <summary>
        /// 得到数字平台工作人员的weixin id
        /// </summary>
        /// <returns></returns>
        public List<WxUserItem> GetDp2003WorkerWeixinIds()
        {
            LibEntity lib = LibDatabase.Current.GetLibByName(WeiXinConst.C_Dp2003LibName);
            return this.getWarningWorkerWeixinIds(lib);
        }

        // 得到图书馆有接收权限的工作人员帐户
        public List<WxUserItem> getWarningWorkerWeixinIds(LibEntity lib)
        {
            List<WxUserItem> adminWeixinIds = new List<WxUserItem>();
            if (lib != null)
            {
                List<WxUserItem> workers = WxUserDatabase.Current.Get("", lib.id, WxUserDatabase.C_Type_Worker);

                // 如果有工作人员配了 接收警告 权限，将只返回有权限的工作人员
                foreach (WxUserItem user in workers)
                {
                    if (user.userName == WxUserDatabase.C_Public)
                        continue;

                    // 从web页面绑定的
                    if (WxUserDatabase.CheckFrom(user.weixinId) == WxUserDatabase.C_from_web)  //.CheckIsFromWeb(user.weixinId) == true) //user.weixinId.Substring(0, 2) == "~~")
                    {
                        this.WriteDebug("工作人员" + user.userName + "的weixinId为" + user.weixinId + ",非微信入口，不发通知。");
                        continue;
                    }

                    bool hasRight = CheckContainRights(user.rights, dp2WeiXinService.C_Right_GetWarning);
                    if (hasRight == true)
                    {
                        adminWeixinIds.Add(user);
                    }
                }

                /*
                2016-10-20 任延华
                修改起因：南开徐老师被不在线通知烦到了，提出改进这个功能，少给图书馆工作人员发通知。
                修改为：如果图书馆工作人员都没有设_wx_getWarning，就不通知了图书馆人员了，只通知数字平台人员。

                // 如果没有一个人配置 接收警告信息 的权限，则发给所有的工作人员
                if (adminWeixinIds.Count == 0)
                {
                    foreach (WxUserItem user in workers)
                    {
                        adminWeixinIds.Add(user);
                    }
                }
                 */
            }

            return adminWeixinIds;
        }

        #endregion

        #region 错误友好提示

        /// <summary>
        /// 提示信息
        /// </summary>
        /// <param name="menu"></param>
        /// <param name="returnUrl"></param>
        /// <returns></returns>
        public static string GetLinkHtml(string menu, string returnUrl, string libName = "")
        {
            return GetLinkHtml(menu, returnUrl, false, libName);
        }


        public static string GetSelLibLink(string state, string returnUrl)
        {


            string bindUrl = "/Patron/SelectOwnerLib?state=" + state + "&returnUrl=" + HttpUtility.UrlEncode(returnUrl);
            string bindLink = "<a href='javascript:void(0)' onclick='gotoUrl(\"" + bindUrl + "\")'>请先点击这里选择图书馆</a>。";
            string strRedirectInfo = "您尚未选择图书馆，" + bindLink;

            strRedirectInfo = "<div class='mui-content-padded' style='color:#666666'>"
                + strRedirectInfo
                + "</div>";
            return strRedirectInfo;
        }


        public static string GetLinkHtml(string menu, string returnUrl, bool bNeedWorker, string libName = "")
        {
            string name = "读者";
            string action = "查看";
            if (bNeedWorker == true)
            {
                name = "工作人员";
                action = "进行";
            }

            string partInfo = "不是" + name + "账户";
            if (menu.IndexOf("借还") != -1)
            {
                partInfo = "权限不足";
            }

            string bindUrl = "/Account/Index?returnUrl=" + HttpUtility.UrlEncode(returnUrl);
            string bindLink = "请先点击<a href='javascript:void(0)' onclick='gotoUrl(\"" + bindUrl + "\")'>这里</a>进行绑定。";
            string strRedirectInfo = "您当前帐户" + partInfo + "，不能" + action + menu + "，" + bindLink;

            if (menu == "书目查询" || menu == "好书推荐" || menu == "公告")
                strRedirectInfo = libName + " 不支持外部访问，您需要先<a href='javascript:void(0)' onclick='gotoUrl(\"" + bindUrl + "\")'>绑定图书馆账户</a>，才能进入" + menu;

            strRedirectInfo = "<div class='mui-content-padded' style='color:#666666'>"
                + strRedirectInfo
                + "</div>";
            return strRedirectInfo;
        }

        /// <summary>
        /// 得到友好的提示
        /// </summary>
        /// <returns></returns>
        //private string GetFriendlyErrorInfo(MessageResult result, string libName)
        //{
        //    if (result.String == "TargetNotFound")
        //    {

        //        // 2016/9/30 在需要的地方再激活吧，放在这里都受影响了。// 激活后面处理线程，可以给工作人员发通知
        //        //this._managerThread.Activate();

        //        return "图书馆 " + libName + " 的桥接服务器失去连接，无法访问。" + result.ErrorInfo;
        //    }

        //    return result.ErrorInfo;
        //}

        /*
    //SearchRequest
    private string GetFriendlyErrorInfo(SearchResult result, string libName, out bool bOffline)
    {
        bOffline = false;
        if (result.ErrorCode == "TargetNotFound")
        {
            // 2016/9/30 在需要的地方再激活吧，放在这里都受影响了。//激活后面处理线程，可以给工作人员发通知
            //this._managerThread.Activate();
            bOffline = true;

            if (String.IsNullOrEmpty(libName) == true)
            {
                return "TargetNotFound";
            }

            return "图书馆 " + libName + " 的桥接服务器失去连接，无法访问。";
        }

        return result.ErrorInfo;
    }
    */

        #endregion

        #region 校验条码

        /*
        校验读者证条码号和册条码号。对应于dp2library的VerifyBarcode() API。
案例1:
Operation:verifyBarcode
Patron:R0000001
Item:海淀分馆
ResultValue返回 0: 不是合法的条码号 1:合法的读者证条码号 2:合法的册条码号。-1表示一般性错误；
         * -2表示dp2library中没有定义VerifyBarcode()脚本函数。
ErrorInfo成员里可能会有报错信息。
         */

        public int VerifyBarcode(string libId,
            string libraryCode,
            string userId,
            string barcode,
            int needTransform,
            out string resultBarcode,
            out string strError)
        {
            int nRet = 0;
            strError = "";

            resultBarcode = barcode;

            string userName = "";
            WxUserItem user = WxUserDatabase.Current.GetById(userId);
            bool bPatron = false;
            if (user.type == WxUserDatabase.C_Type_Patron)
            {
                userName = user.readerBarcode;
                bPatron = true;
            }
            else
            {
                userName = user.userName;
                bPatron = false;
            }

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆。";
                goto ERROR1;
            }

            // 使用代理账号capo 20161024 jane
            LoginInfo loginInfo = new LoginInfo(userName, bPatron);

            // 对于分馆，检查条码是否加前缀。
            if (string.IsNullOrEmpty(libraryCode) == false && needTransform == 1)
            {
                //string resultBarcode = "";
                nRet = this.GetTransformBarcode(loginInfo,
                    libId,
                    libraryCode,
                    barcode,
                    out resultBarcode,
                    out strError);
                if (nRet == -1)
                    return -1;

                // 转换过的
                if (nRet == 1)
                    barcode = resultBarcode;
            }



            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            CirculationRequest request = new CirculationRequest(id,
                loginInfo,
                "verifyBarcode",
                barcode,
                user.libraryCode,//this.textBox_circulation_item.Text,
                "",//this.textBox_circulation_style.Text,
                "",//this.textBox_circulation_patronFormatList.Text,
                "",//this.textBox_circulation_itemFormatList.Text,
                "");//this.textBox_circulation_biblioFormatList.Text);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                CirculationResult result = connection.CirculationTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 10), // 10 秒
                    cancel_token).Result;

                nRet = (int)result.Value;
                if (result.Value == -1)
                {
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;
                    //strError = //this.GetFriendlyErrorInfo(result, lib.libName); //result.ErrorInfo;
                }
                if (result.Value == 0)
                {
                    // 试试是否是为读者
                    string patronRecPath = "";
                    string timestamp = "";
                    string patronXml = "";
                    nRet = dp2WeiXinService.Instance.GetPatronXml(libId,
                        loginInfo,
                        barcode,
                        "xml",
                        out patronRecPath,
                        out timestamp,
                        out patronXml,
                        out strError);
                    if (nRet == 1)
                    {
                        nRet = 1;
                    }
                    else
                    {
                        strError = result.ErrorInfo; //"不是合法的条码号";// 
                    }
                }

                strError = HttpUtility.HtmlEncode(strError);
                return nRet;

            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }

        ERROR1:

            return -1;
        }

        public int GetTransformBarcode(LoginInfo loginInfo,
            string libId,
            string libraryCode,
            string barcode,
            out string resultBarcode,
            out string strError)
        {
            int nRet = 0;
            strError = "";
            resultBarcode = barcode;
            int transform = dp2WeiXinService.Instance.CheckIsTransformBarcode(libId, libraryCode);
            if (transform == -1) //不清楚时，设?transform接口
            {
                //调接口
                //Operation:verifyBarcode
                //Style:transform或者TransformBarcode
                //Item:海淀分馆
                //Patron:?transform
                //ResultValue返回 
                //0:“海淀分馆”的条码号不需要进行变换 
                //1:“海淀分馆”的条码号需要发生变换。
                //-1表示一般性错误；
                //-2表示dp2library的library.xml中没有定义TransformBarcode()脚本函数。
                nRet = this.TransformBarcode(loginInfo,
                    libId,
                    libraryCode,
                    "?transform",
                    out resultBarcode,
                    out strError);

                if (strError.IndexOf("<script>") != -1)
                    strError = strError.Replace("<script>", "");
                if (nRet == -1)
                    return -1;

                if (nRet == 1)
                    transform = 1;
                else
                    transform = 0;

                dp2WeiXinService.Instance.SetTransformBarcode(libId, libraryCode, transform);
            }

            //需要转换
            if (transform == 1)
            {
                nRet = this.TransformBarcode(loginInfo,
                    libId,
                    libraryCode,
                    barcode,
                    out resultBarcode,
                    out strError);
                if (nRet == -1)
                    return -1;
            }

            return nRet;
        }

        //0:条码号没有发生变换 
        //1:条码号发生了变换(此时成员PatronBarcode里面返回变换以后的条码号)。
        //-1表示一般性错误；
        //-2表示dp2library的library.xml中没有定义TransformBarcode()脚本函数。
        public int TransformBarcode(LoginInfo loginInfo,
            string libId,
            string libraryCode,
            string barcode,
            out string resultBarcode,
            out string strError)
        {
            int nRet = 0;
            strError = "";
            resultBarcode = barcode;


            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆。";
                goto ERROR1;
            }



            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            CirculationRequest request = new CirculationRequest(id,
                loginInfo,
                "verifyBarcode",
                barcode, //patron
                libraryCode,//this.textBox_circulation_item.Text,
                "TransformBarcode",//this.textBox_circulation_style.Text,
                "",//this.textBox_circulation_patronFormatList.Text,
                "",//this.textBox_circulation_itemFormatList.Text,
                "");//this.textBox_circulation_biblioFormatList.Text);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                CirculationResult result = connection.CirculationTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 10), // 10 秒
                    cancel_token).Result;

                nRet = (int)result.Value;
                if (result.Value == -1)
                {
                    strError = "图书馆[" + libraryCode + "]返回错误:" + result.ErrorInfo;
                    //strError = this.GetFriendlyErrorInfo(result, libraryCode);//lib.libName); 
                    return -1;
                }
                if (result.Value == -2)
                {
                    strError = "没有定义TransformBarcode()脚本函数";
                    return 0;
                }
                if (result.Value == 0)
                {
                    resultBarcode = barcode;
                }
                if (result.Value == 1)
                {
                    resultBarcode = result.PatronBarcode;
                }

                return nRet;

            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }

        ERROR1:

            return -1;
        }
        #endregion

        #region 找回密码，修改密码，二维码



        /// <summary>
        /// 合并读者信息，将修改的某几个字段与读者原有信息进行合并
        /// </summary>
        /// <param name="libId">图书馆id</param>
        /// <param name="recPath">读者记录</param>
        /// <param name="patron">传过来的对象，包含要修改的信息</param>
        /// <param name="checkNull">是否检查patron对象的字段为空（null或空字符串），如果checkNull==true则表示,字段为空时，不进行修改</param>
        /// <param name="bWorker">是否是工作人员进行操作，影响备注字段的表达</param>
        /// <param name="patronXml">输出合并后的xml</param>
        /// <param name="timestamp">读者时间戳</param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int MergePatronXml(LoginInfo loginInfo,
            string libId,
            string recPath,
            SimplePatron patron,
            bool checkNull,
             bool bWorker,
             bool bReviewPass,   // 2020/12/23 增加是否是审核通过，如果是的话，设置办证日期
            out string patronXml,
            out string timestamp,
            out string strError)
        {
            strError = "";
            patronXml = "";

            // 2021/8/5 注释掉，改为调用者传入的loginInfo
            // 统一用代理帐户检索,从服务器检索读者原来信息
            //LoginInfo loginInfo = new LoginInfo("", false);


            string strOldXml = "";
            string word = "@path:" + recPath;
            string tempPath = "";
            //string tempTimestamp = "";
            int nRet = dp2WeiXinService.Instance.GetPatronXml(libId,
                loginInfo,
                word,
                "xml,timestamp",
                out tempPath,
                out timestamp,
                out strOldXml,
                out strError);
            if (nRet == -1 || nRet == 0)
                return -1;

            // 把原始读者信息装载到dom
            XmlDocument dom = new XmlDocument();
            dom.LoadXml(strOldXml);
            XmlNode root = dom.DocumentElement;


            // 读者证条码号，2020-3-17 先检查是否需要修改
            if (CheckFieldIsNeedChange(checkNull, patron.barcode) == true)
            {
                XmlNode barcodeNode = root.SelectSingleNode("barcode");
                if (barcodeNode == null)
                    barcodeNode = DomUtil.CreateNodeByPath(root, "barcode");
                DomUtil.SetNodeText(barcodeNode, patron.barcode);
            }

            // 读者类型，2020-3-17 先检查是否需要修改
            if (CheckFieldIsNeedChange(checkNull, patron.readerType) == true)
            {
                XmlNode readerTypeNode = root.SelectSingleNode("readerType");
                if (readerTypeNode == null)
                    readerTypeNode = DomUtil.CreateNodeByPath(root, "readerType");
                DomUtil.SetNodeText(readerTypeNode, patron.readerType);
            }

            // 姓名，2020-3-17 先检查是否需要修改
            if (CheckFieldIsNeedChange(checkNull, patron.name) == true)
            {
                XmlNode nameNode = root.SelectSingleNode("name");
                if (nameNode == null)
                    nameNode = DomUtil.CreateNodeByPath(root, "name");
                DomUtil.SetNodeText(nameNode, patron.name);
            }

            // 性别
            // 2020-3-17 加检查是否是空值，
            if ((checkNull == false)
                || ((checkNull == true) && string.IsNullOrEmpty(patron.gender) == false))
            {
                XmlNode genderNode = root.SelectSingleNode("gender");
                if (genderNode == null)
                    genderNode = DomUtil.CreateNodeByPath(root, "gender");
                DomUtil.SetNodeText(genderNode, patron.gender);
            }

            // 部门，2020-3-17 先检查是否需要修改
            if (CheckFieldIsNeedChange(checkNull, patron.department) == true)
            {
                XmlNode departmentNode = root.SelectSingleNode("department");
                if (departmentNode == null)
                    departmentNode = DomUtil.CreateNodeByPath(root, "department");
                DomUtil.SetNodeText(departmentNode, patron.department);
            }

            // 手机号，2020-3-17 先检查是否需要修改
            if (CheckFieldIsNeedChange(checkNull, patron.phone) == true)
            {
                XmlNode telNode = root.SelectSingleNode("tel");
                if (telNode == null)
                    telNode = DomUtil.CreateNodeByPath(root, "tel");
                DomUtil.SetNodeText(telNode, patron.phone);
            }

            // 状态 state，2020-3-17 先检查是否需要修改
            if (CheckFieldIsNeedChange(checkNull, patron.state) == true)
            {
                XmlNode stateNode = root.SelectSingleNode("state");
                if (stateNode == null)
                    stateNode = DomUtil.CreateNodeByPath(root, "state");
                DomUtil.SetNodeText(stateNode, patron.state);
            }

            // 把不通过的原因放在注释里，2020-3-17 先检查是否需要修改
            if (CheckFieldIsNeedChange(checkNull, patron.comment) == true)
            {
                // 备注是一直累加的
                string oldComment = "";
                XmlNode commentNode = root.SelectSingleNode("comment");
                if (commentNode == null)
                    commentNode = DomUtil.CreateNodeByPath(root, "comment");
                else
                    oldComment = DomUtil.GetNodeText(commentNode);

                // 本次comment的完整表达
                string thisComment = this.GetFullComment(patron.comment, bWorker);

                DomUtil.SetNodeText(commentNode, oldComment + "\r\n" + thisComment);
            }

            // 2020/12/23 增加通过
            if (bReviewPass == true)
            {
                string strCreateDate = DateTimeUtil.Rfc1123DateTimeString(DateTime.Now);// GetRfc1123DisplayString(
                DomUtil.SetElementText(root, "createDate", strCreateDate);
            }

            StringWriter textWrite = new StringWriter();
            XmlTextWriter xmlTextWriter = new XmlTextWriter(textWrite);
            dom.Save(xmlTextWriter);
            patronXml = textWrite.ToString();

            return 0;
        }

        /// <summary>
        /// 得到完整的备注信息
        /// </summary>
        /// <param name="comment"></param>
        /// <param name="bWorker"></param>
        /// <returns></returns>
        private string GetFullComment(string comment, bool bWorker)
        {
            string thisComment = "";
            string dateTime = DateTimeUtil.DateTimeToString(DateTime.Now);
            if (string.IsNullOrEmpty(comment) == false)
            {
                if (bWorker == true)
                    thisComment = dateTime + " 馆员备注:" + comment;
                else
                    thisComment = dateTime + " 读者备注:" + comment;
            }

            return thisComment;
        }

        /// <summary>
        /// 2020-3-17 检查读者字段是否需要修改，如果只修改某个字段，其它字段会传空值，则不需要修改。
        /// </summary>
        /// <param name="checkNull">是否检查空</param>
        /// <param name="field">字段值</param>
        /// <returns></returns>
        public bool CheckFieldIsNeedChange(bool checkNull, string field)
        {
            if (checkNull == false)
                return true;

            if (checkNull == true && string.IsNullOrEmpty(field) == false)
            {
                return true;
            }

            return false;
        }

        // dp2library的SetReaderInfo的action
        public const string C_Action_new = "new";
        public const string C_Action_change = "change";
        public const string C_Action_changestate = "changestate";
        public const string C_Action_delete = "delete";

        // 公众号这边针对读者的操作类型
        public const string C_OpeType_register = "register"; //读者自助注册，转换为new
        public const string C_OpeType_reRegister = "reRegister"; //读者重新提交注册，转换为change,当状态是"待审核"，读者类型为空时才能保存成功
        public const string C_OpeType_reviewPass = "reviewPass";  //馆员审核通过，转换为change
        public const string C_OpeType_reviewNopass = "reviewNopass"; //馆员审核不通过,转换为changestate
        public const string C_OpeType_reviewNopassDel = "reviewNopassDel"; //馆员审核不通过+删除，转换为delete
        public const string C_OpeType_changeByPatron = "changeByPatron"; //读者自己修改信息，转换为change,注意只允许读者修改几个字段，其它字段传过来为null，表示不修改
        public const string C_OpeType_deleteByPatron = "deleteByPatron"; //读者自己删除信息，转换为delete,只有当状态为"待审核"或者"审核不通过"时，读者才能自己删除记录
        public const string C_OpeType_newByWorker = "newByWorker"; //馆员登记读者，转换为new
        public const string C_OpeType_changeByWorker = "changeByWorker"; //馆员修改读者信息，转换为change


        /// <summary>
        /// 设置读者信息
        /// </summary>
        /// <param name="libId">图书馆id，找本地mongodb的id</param>
        /// <param name="userName">工作人员帐号名</param>
        /// <param name="opeType">操作类型：
        /// 1 register:读者自助注册，转换为new
        /// 2 reRegister:读者重新提交注册，转换为change,当状态是"待审核"，读者类型为空时才能保存成功
        /// 3 reviewPass:馆员审核通过，转换为change
        /// 4 reviewNopass:馆员审核不通过,转换为changestate
        /// 5 reviewNopassDel:馆员审核不通过+删除，转换为delete
        /// 6 changeByPatron:读者自己修改信息，转换为change,注意只允许读者修改几个字段，其它字段传过来为null，表示不修改
        /// 7 deleteByPatron:读者自己删除信息，转换为delete,只有当状态为"待审核"或者"审核不通过"时，读者才能自己删除记录
        /// 8 newByWorker:馆员登记读者，转换为new
        /// 9 changeByWorker:馆员修改读者信息，转换为change
        /// </param>
        /// <param name="recPath">读者记录路径</param>
        /// <param name="timestamp">读者记录时间戳</param>
        /// <param name="weixinId">读者weixinId，当opeType=register/reRegister时有值</param>
        /// <param name="patron">传递的读者信息对象</param>
        /// <param name="outputRecPath">返回的读者路径</param>
        /// <param name="outputTimestamp">返回的读者记录时间戳</param>
        /// <param name="userItem">返回的本地库对象</param>
        /// <param name="strError">出错信息</param>
        /// <returns>
        /// -1 出错
        /// 0 正常
        /// </returns>
        public  int SetReaderInfo(LoginInfo loginInfo,
            string libId,
            string userName,
            string opeType,
            string recPath,
            string timestamp,
            string weixinId,
            SimplePatron patron,
            out string outputRecPath,
            out string outputTimestamp,
            out WxUserItem userItem2,  //只有当读者注册时才能值，馆员编辑读者时返回null
            out string strError)
        {
            strError = "";
            outputRecPath = "";
            outputTimestamp = "";
            userItem2 = null;
            int nRet = 0;

            bool bReviewPass = false;

            //===
            //将前端传的操作类型转换为dp2可以识别的action
            string action = "";
            switch (opeType)
            {
                case C_OpeType_register:
                    action = C_Action_new;
                    break;
                case C_OpeType_reRegister:
                    action = C_Action_change;
                    break;
                case C_OpeType_reviewPass:
                    action = C_Action_change;
                    bReviewPass = true;
                    break;
                case C_OpeType_reviewNopass:
                    action = C_Action_changestate;
                    break;
                case C_OpeType_reviewNopassDel:
                    action = C_Action_delete;
                    break;
                case C_OpeType_changeByPatron:
                    action = C_Action_change;
                    break;
                case C_OpeType_deleteByPatron:
                    action = C_Action_delete;
                    break;
                case C_OpeType_newByWorker:
                    action = C_Action_new;
                    break;
                case C_OpeType_changeByWorker:
                    action = C_Action_change;
                    break;
                default:
                    break;
            }

            if (action == "")
            {
                strError = "无法将opeType=" + opeType + "转换为action";
                return -1;
            }

            //===
            // 根据id找到图书馆对象
            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            // 2021/8/2 注释掉，改为传进来的对象
            //// 读者自助注册时使用代理账号capo 2020/1/21
            bool bWorker = false;
            //LoginInfo loginInfo = new LoginInfo("", false);
            if (string.IsNullOrEmpty(userName) == false)
            {
                //loginInfo = new LoginInfo(userName, false);
                bWorker = true;
            }

            string patronXml = "";
            if (action == C_Action_new)
            {
                // 如果传入weixinid，则表示是读者自助注册，需要给email字段设上weixinId，建立绑定关系
                string email = "";
                if (string.IsNullOrEmpty(weixinId) == false)
                {
                    email = "weixinid:" + weixinId;//传入的weixinId带着后缀 + "@" + wxAppId;
                }

                // 如果是馆员登记的读者，状态不设为 待审核
                string patronState = "";
                if (opeType != C_OpeType_newByWorker)
                {
                    patronState = WxUserDatabase.C_PatronState_TodoReview;
                }

                // 将读者提交的备注，转换成完整表达，例如 2020/6/3 14:02 读者备注:我是办公室的。
                // 备注字段一般是累积增加的。
                string thisComment = this.GetFullComment(patron.comment, bWorker);
                patronXml = "<root>"
                       + "<barcode>" + patron.barcode + "</barcode>"
                       + "<state>" + patronState + "</state> "
                       + "<readerType>" + patron.readerType + "</readerType>"
                       + "<name>" + patron.name + "</name>"
                       + "<gender>" + patron.gender + "</gender>"
                       + "<department>" + patron.department + "</department>"
                       + "<tel>" + patron.phone + "</tel>"
                       + "<email>" + email + "</email>"
                       + "<comment>" + thisComment + "</comment>"
                       + "</root>";
            }
            else if (action == C_Action_change || action == C_Action_changestate)
            {
                bool bCheckNull = false;
                // 如果是读者自己修改个人信息，例如手机号。一般情况下只让读者修改小部分字段，那么作为参数传进来的读者对象，其它字段是传的null，
                // 检查发现字段是null,则原来的字段信息不变。
                if (opeType == C_OpeType_changeByPatron)
                {
                    bCheckNull = true;
                }

                // 将dp2服务器的读者信息与界面提交的信息合并
                nRet = this.MergePatronXml(loginInfo,
                    libId,
                    recPath,
                    patron,
                    bCheckNull,
                    bWorker,
                    bReviewPass,
                    out patronXml,
                    out timestamp,
                    out strError);
                if (nRet == -1)
                    return -1;
            }
            else if (action == C_Action_delete)
            {
                //删除
            }
            else
            {
                strError = "SetReaderInfo()接口不能识别的action[" + action + "]";
                return -1;
            }

            // 点对点消息实体
            /*
            public class Entity
            {
                public string Action { get; set; }
                public string RefID { get; set; }
                public Record OldRecord { get; set; }
                public Record NewRecord { get; set; }
                public string Style { get; set; }
                public string ErrorInfo { get; set; }
                public string ErrorCode { get; set; }
            }

            public class Record
            {
                public string RecPath { get; set; }
                public string Format { get; set; }
                public string Data { get; set; }
                public string Timestamp { get; set; }
            }
            */
            Record newRecord = new Record();
            newRecord.RecPath = recPath;
            newRecord.Format = "xml";
            newRecord.Data = patronXml;

            Entity entity = new Entity();
            entity.Action = action;
            entity.NewRecord = newRecord;

            List<Entity> entities = new List<Entity>();
            entities.Add(entity);

            // 修改 或 删除 读者，需要传入老记录和时间戳
            if (action != C_Action_new)
            {
                Record oldRecord = new Record();
                oldRecord.Timestamp = timestamp;
                entity.OldRecord = oldRecord;
            }

            // 接口返回的读者xml
            string outputPatronXml = "";

            // 调点对点接口
            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SetInfoRequest request = new SetInfoRequest(id,
                loginInfo,
                "setReaderInfo",
                "", //biblioRecPath
                entities);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    "").Result;

                SetInfoResult result = connection.SetInfoTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    strError = "图书馆 " + lib.libName + " 操作出错:" + result.ErrorInfo;
                    return -1;
                }

                // 取出读者信息
                if (result.Entities.Count > 0)
                {
                    outputRecPath = result.Entities[0].NewRecord.RecPath;
                    outputTimestamp = result.Entities[0].NewRecord.Timestamp;
                    outputPatronXml = result.Entities[0].NewRecord.Data;


                }

            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }



            // 如果是普通读者自己修改信息（目前只是修改手机号），系统发微信通知管理员。注意这里不是读者重新提交注册
            if (opeType == C_OpeType_changeByPatron)
            {
                /*
张三客户经理，您有客户更新了基本信息，更新情况如下：
用户名：李四
联系方式：13788888888
变更类型：修改手机号
变更时间：2016年1月2日 14:00
请登录系统查看该客户的详细更新情况！
                 */

                this.WriteDebug("设置读者接口返回的xml为：" + outputPatronXml);

                WxUserItem patronInfo = this.GetPatronInfoByXml(outputPatronXml, out List<string> tempWeixinIds);

                // 如果不是待审核的读者，会给馆员发通知。
                if (patronInfo.patronState != WxUserDatabase.C_PatronState_TodoReview)
                {
                    string strFirst = "您好，下面读者更新了手机号信息。";
                    string strRemark = "请登录图书馆系统查看该读者的详细信息。";

                    string patronName = patronInfo.readerName;

                    // todo
                    GzhCfg gzh = dp2WeiXinService.Instance._gzhContainer.GetDefault();//.GetByAppId(this.AppId);
                    if (gzh == null)
                    {
                        strError = "未找到默认的公众号配置";
                        return -1;
                    }
                    string linkUrl = "";


                    //// 不发本人
                    List<WxUserItem> bindPatronList = new List<WxUserItem>();

                    // 工作人员
                    // 2020-3-17 发给本馆绑定的工作人员，不管是否打开监控消息没有关系，
                    //只要图书馆工作人员绑定了帐户，就发给馆员。也不再发给dp2003的监控，意义不大。
                    string libraryCode = patronInfo.libraryCode;
                    if (libraryCode == "")
                        libraryCode = "空";

                    this.WriteDebug("检索工作人员条件libId=["+libId+"],libraryCode=["+libraryCode+"]");
                    List<WxUserItem> tempWorkers = WxUserDatabase.Current.GetWorkers("", libId, libraryCode, "");
                    this.WriteDebug("共检索到" + tempWorkers.Count + "个工作人员");
                    List<WxUserItem> workers = new List<WxUserItem>();
                    foreach (WxUserItem one in tempWorkers)
                    {
                        //this.WriteDebug("工作人员详情："+one.Dump());

                        // 2020-3-11 还要加上public帐户
                        // web来源，小程序来源，public帐户不需要发微信通知
                        if (WxUserDatabase.CheckFrom(one.weixinId) == WxUserDatabase.C_from_web
                            || WxUserDatabase.CheckFrom(one.weixinId) == WxUserDatabase.C_from_applet
                           || one.userName == WxUserDatabase.C_Public)
                        {
                            continue;
                        }

                        workers.Add(one);
                    }

                    if (bindPatronList.Count > 0 || workers.Count > 0)
                    {
                        // 不加mask的通知数据
                        string thisTime = dp2WeiXinService.GetNowTime();
                        string first_color = "#000000";
                        ReviewPatronTemplateData msgData = new ReviewPatronTemplateData(strFirst, first_color,
                            patronName, patronInfo.phone, "修改手机号", thisTime,
                            strRemark);

                        // 发送待审核的微信消息
                        nRet = this.SendTemplateMsg1(GzhCfg.C_Template_PatronInfoChanged,
                           bindPatronList,
                           workers,
                           msgData,
                           msgData, //用的同一组数据，不做马赛克
                           this._messageLinkUrl,//linkUrl,
                           "",
                           false,
                           "",// originalXml  2023/6/7
                           out strError);
                        if (nRet == -1)
                        {
                            return -1;
                        }
                    }
                }
            }


            // 如果是读者自助的情况，给本地mongodb创建一笔绑定记录
            if (opeType == C_OpeType_register)
            {
                string bindLibraryCode = patron.libraryCode;

                // 要使用返回的读者信息，因为前端组装的xml没有refID
                nRet = this.SaveUserToLocal1(weixinId,
                    libId,
                    bindLibraryCode,
                    C_TYPE_READER,
                    outputRecPath,
                    outputPatronXml,
                    "new",
                    true,
                    out userItem2,
                    out strError);
                if (nRet == -1)
                {
                    return -1;
                }

                // 给馆员发送微信通知（需要馆员先绑定微信帐户，然后监控本馆消息），
                // 同时消息也发给打开了监控数字平台工作
                // 给馆员发消息似乎不用关心是否是web入口
                // 发送绑定成功的客服消息   
                {
                    /*
                    您好，您有新的读者注册信息待审核。
                    申请人：任延华
                    手机号码：13862157150
                    申请进度：等待审核
                    申请时间：2020/02/29 15:10
                    审核通过后，读者才能借还书。
                     */
                    string strFirst = "您好，您有新的读者注册信息待审核";
                    string strRemark = "审核通过后，读者才能借还书。";

                    //string strAccount = this.GetFullPatronName(userItem.readerName, 
                    //    userItem.readerBarcode, "", "", false);
                    string strAccount = userItem2.readerName;
                    // string fullLibName = this.GetFullLibName(userItem.libName, userItem.libraryCode, "");

                    // todo
                    GzhCfg gzh = dp2WeiXinService.Instance._gzhContainer.GetDefault();//.GetByAppId(this.AppId);
                    if (gzh == null)
                    {
                        strError = "未找到默认的公众号配置";
                        return -1;
                    }
                    string linkUrl = "";//dp2WeiXinService.Instance.OAuth2_Url_AccountIndex,//详情转到账户管理界面
                                        //linkUrl = dp2WeiXinService.Instance.GetOAuth2Url(gzh,
                                        //    "Patron/PatronReview?libId=" + userItem.libId
                                        //    + "&patronLibCode=" + HttpUtility.UrlEncode(userItem.bindLibraryCode)
                                        //    + "&patronPath=" + HttpUtility.UrlEncode(userItem.recPath)
                                        //    + "&f=notice"
                                        //    );

                    linkUrl = dp2WeiXinService.Instance.GetOAuth2Url(gzh,
                        HttpUtility.UrlEncode("Patron/PatronReview?libId=" + userItem2.libId
                            + "&patronLibCode=" + userItem2.bindLibraryCode
                            + "&patronPath=" + userItem2.recPath
                            + "&barcode=" + userItem2.readerBarcode
                            + "&f=notice")
                        );

                    //this.WriteDebug("链接地址-"+linkUrl);

                    //// 本人
                    List<WxUserItem> bindPatronList = new List<WxUserItem>();
                    //string tempfullWeixinId = weixinId;//2016-11-16 传进来的weixinId带了@appId // +"@" + appId;
                    //bindWeixinIds.Add(tempfullWeixinId);

                    // 工作人员
                    //List<WxUserItem> workers = this.GetTraceUsers(libId, userItem.libraryCode);

                    // 2020-3-9 改为发给本馆绑定的工作人员，不管是否打开监控消息没有关系，
                    //只要图书馆工作人员绑定了帐户，就发给馆员。也不再发给dp2003的监控，意义不大。
                    string libraryCode = userItem2.libraryCode;
                    if (libraryCode == "")
                        libraryCode = "空";
                    List<WxUserItem> tempWorkers = WxUserDatabase.Current.GetWorkers("", libId, libraryCode, "");
                    List<WxUserItem> workers = new List<WxUserItem>();
                    foreach (WxUserItem one in tempWorkers)
                    {
                        // 2020-3-11 还要加上public帐户
                        // 2022/07/20 web入口、小程序入口，public帐户，均不发微信通知
                        if (WxUserDatabase.CheckFrom(one.weixinId) == WxUserDatabase.C_from_web
                            || WxUserDatabase.CheckFrom(one.weixinId) == WxUserDatabase.C_from_applet
                            || one.userName == WxUserDatabase.C_Public)
                            continue;

                        workers.Add(one);
                    }

                    if (bindPatronList.Count > 0 || workers.Count > 0)
                    {
                        // 不加mask的通知数据
                        string thisTime = dp2WeiXinService.GetNowTime();
                        string first_color = "#000000";
                        ReviewPatronTemplateData msgData = new ReviewPatronTemplateData(strFirst, first_color,
                            strAccount, userItem2.phone, "等待审核", thisTime,
                            strRemark);

                        ////加mask的通知数据
                        //strAccount = this.GetFullPatronName(userItem.readerName, userItem.readerBarcode, "", "", true);//this.markString(userItem.readerName) + " " + this.markString(userItem.readerBarcode) + "";
                        //ReviewPatronTemplateData maskMsgData = new ReviewPatronTemplateData(strFirst, first_color,
                        //    strAccount, userItem.phone, "等待审核", thisTime,
                        //    strRemark);

                        // 发送待审核的微信消息
                        nRet = this.SendTemplateMsg1(GzhCfg.C_Template_ReviewPatron,
                           bindPatronList,
                           workers,
                           msgData,
                           msgData, //用的同一组数据，不做马赛克
                           linkUrl,  //注意这里有专用的url
                           "",
                           false,
                           "",// originalXml  2023/6/7
                           out strError);
                        if (nRet == -1)
                        {
                            return -1;
                        }
                    }
                }
            }


            // 馆员审核的情况，给读者发通知
            if (opeType == C_OpeType_reviewPass
                || opeType == C_OpeType_reviewNopass
                || opeType == C_OpeType_reviewNopassDel)
            {
                //尊敬的用户：您好！您提交的书柜帐户注册信息已审核完成。
                //申请人：张三
                //手机号码：13866668888
                //审核结果：通过
                //您现在使用智能书柜借书了。

                string strFirst = "尊敬的用户：您好！您提交的注册信息已审核完成。";
                string strRemark = "您现在可以使用智能书柜或到图书馆借还书了。";
                string result = "通过";
                if (opeType == C_OpeType_reviewNopass)
                {
                    result = "不通过";
                    strRemark = "审核不通过原因：" + patron.comment;
                }
                else if (opeType == C_OpeType_reviewNopassDel)
                {
                    result = "不通过,注册帐号已被管理员删除。";
                    strRemark = "审核不通过原因：" + patron.comment;
                }

                // 查找到本地库的记录
                List<WxUserItem> userList = WxUserDatabase.Current.GetPatron(null,
                    libId,
                    patron.barcode);
                foreach (WxUserItem user in userList)
                {
                    // todo，这里使用的default公众号，后面如果有些单位用自己的公众号再改进
                    GzhCfg gzh = dp2WeiXinService.Instance._gzhContainer.GetDefault();//.GetByAppId(this.AppId);
                    if (gzh == null)
                    {
                        strError = "未找到默认的公众号配置";
                        return -1;
                    }

                    // 到我的信息
                    string linkUrl = dp2WeiXinService.Instance.GetOAuth2Url(gzh, "Patron/PersonalInfo");


                    //// 本人
                    //List<string> bindWeixinIds = new List<string>();
                    //bindWeixinIds.Add(userItem.weixinId);
                    List<WxUserItem> bindPatronList = new List<WxUserItem>();
                    bindPatronList.Add(user);

                    // 2020-3-8 改为不给工作人员发消息，怕把管理员弄混了。
                    // 工作人员
                    List<WxUserItem> workers = new List<WxUserItem>(); //this.GetTraceUsers(userItem.libId, userItem.libraryCode);

                    if (bindPatronList.Count > 0 || workers.Count > 0)
                    {
                        // 不加mask的通知数据
                        string thisTime = dp2WeiXinService.GetNowTime();
                        string first_color = "#000000";
                        ReviewResultTemplateData msgData = new ReviewResultTemplateData(strFirst,
                            first_color,
                            user.readerName,
                            user.phone,
                            result,
                            strRemark);


                        // mask 2021/8/3
                        string maskdef = this.GetMaskDef(user.libId, user.libraryCode, out bool send2PatronIsMask);
                        string maskReaderName = dp2WeiXinService.Mask(maskdef, user.readerName, "name");
                        string maskPhone = dp2WeiXinService.Mask(maskdef, user.phone, "barcode");
                        ReviewResultTemplateData maskMsgData = new ReviewResultTemplateData(strFirst,
                            first_color,
                            maskReaderName,
                            maskPhone,
                            result,
                            strRemark);

                        // 发送消息
                        nRet = this.SendTemplateMsg1(GzhCfg.C_Template_ReviewResult,
                           bindPatronList,
                           workers,
                           msgData,
                           maskMsgData,
                           linkUrl,  // 专用地址
                           "",
                           send2PatronIsMask,
                           "",// originalXml  2023/6/7
                           out strError);
                        if (nRet == -1)
                        {
                            return -1;
                        }
                    }
                }
            }

            // 删除记录要放在最后，有一种情况是审核不通过+删除，审核不通过先要发完通知，才能删除
            // 另一种情况是自己自己删除，这种情况不需要发消息，现在前端卡了只有待审核和审核不通过才能删除
            // 如果后面放开任何状态读者都可以删除的话，删除记录要给馆员发通知。
            if (action == C_Action_delete)
            {
                // 查找到本地库的记录
                List<WxUserItem> userList = WxUserDatabase.Current.GetPatron(null,
                    libId,
                    patron.barcode);
                foreach (WxUserItem user in userList)
                {
                    // 删除mongodb库的记录
                    WxUserDatabase.Current.Delete1(user.id, out WxUserItem newActiveUser);

                    // 读者自己删除，且状态不是待审核和审核不通过，则给馆员发通知。
                    if (opeType == C_OpeType_deleteByPatron
                        && user.patronState != WxUserDatabase.C_PatronState_TodoReview
                        && user.patronState != WxUserDatabase.C_PatronState_NoPass)
                    {
                        //您好，下面读者删除了个人信息。
                        //用户名：李四
                        //联系方式：13788888888
                        //变更类型：删除读者记录
                        //变更时间：2016年1月2日 14:00
                        string strFirst = "您好，下面读者删除了个人信息。";
                        string strRemark = "请登录图书馆系统查看操作日志。";
                        string patronName = user.readerName;

                        // todo 会改为根据帐户对应的公众号来发消息
                        GzhCfg gzh = dp2WeiXinService.Instance._gzhContainer.GetDefault();//.GetByAppId(this.AppId);
                        if (gzh == null)
                        {
                            strError = "未找到默认的公众号配置";
                            return -1;
                        }
                        string linkUrl = ""; // 删除的记录，没有链接地址了。


                        //// 不发本人
                        List<WxUserItem> bindPatronList = new List<WxUserItem>();

                        // 工作人员
                        // 2020-3-17 发给本馆绑定的工作人员，不管是否打开监控消息没有关系，
                        //只要图书馆工作人员绑定了帐户，就发给馆员。也不再发给dp2003的监控，意义不大。
                        string libraryCode = user.libraryCode;
                        if (libraryCode == "")
                            libraryCode = "空";
                        List<WxUserItem> tempWorkers = WxUserDatabase.Current.GetWorkers("", libId, libraryCode, "");
                        List<WxUserItem> workers = new List<WxUserItem>();
                        foreach (WxUserItem worker in tempWorkers)
                        {
                            // 2020-3-11 非微信入口和public不发通知
                            // 2022/07/20 web入口、小程序入口，public帐户，均不发微信通知
                            if (WxUserDatabase.CheckFrom(worker.weixinId) == WxUserDatabase.C_from_web
                                || WxUserDatabase.CheckFrom(worker.weixinId) == WxUserDatabase.C_from_applet
                                    || worker.userName == WxUserDatabase.C_Public)
                                continue;

                            workers.Add(worker);
                        }

                        if (bindPatronList.Count > 0 || workers.Count > 0)
                        {
                            // 不加mask的通知数据
                            string thisTime = dp2WeiXinService.GetNowTime();
                            string first_color = "#000000";
                            ReviewPatronTemplateData msgData = new ReviewPatronTemplateData(strFirst, first_color,
                                patronName, user.phone, "删除读者记录", thisTime,
                                strRemark);

                            // 发送一条修改读者信息的微信通知
                            nRet = this.SendTemplateMsg1(GzhCfg.C_Template_PatronInfoChanged,
                               bindPatronList,
                               workers,
                               msgData,
                               msgData, //用的同一组数据，不做马赛克
                               linkUrl,
                               "",
                               false,
                               "",// originalXml  2023/6/7
                               out strError);
                            if (nRet == -1)
                            {
                                return -1;
                            }
                        }
                    }

                }
            }

            return 0;

        }


        /// <summary>
        /// 获取临时读者
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="patronList"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int GetTempPatrons(LoginInfo loginInfo,
            string libId,
            string libraryCode,
            out List<Patron> patronList,
            out string strError)
        {
            strError = "";
            patronList = new List<Patron>();
            if (libraryCode == null)
                libraryCode = "";

            // 根据id找到图书馆对象
            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            // 使用代理账号
            //LoginInfo loginInfo = new LoginInfo("", false);

            // 从dp2library中查
            string strWord = WxUserDatabase.C_PatronState_TodoReview;  // 待审核
            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SearchRequest request = new SearchRequest(id,
                loginInfo,
                "searchPatron",
                "<全部>",  //todo 这里后面考虑是否指定总分馆对应的数据库
                strWord,
                "state",  //状态 途径
                "left",
                "temp-patron",
                "id,xml",
                1000,
                0,
                WeiXinConst.C_Search_MaxCount);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                SearchResult result = connection.SearchTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.ResultCount == -1)
                {
                    strError = "图书馆 " + lib.libName + "检索临时读者出错:" + result.ErrorInfo;
                    return -1;
                }
                if (result.ResultCount == 0)
                    return 0;
                foreach (Record record in result.Records)// int i = 0; i < result.ResultCount; i++)
                {
                    string xml = record.Data;

                    Patron patron = this.ParsePatronXml(libId,
                         xml,
                         this.GetPurePath(record.RecPath),
                         0,
                         false);

                    if (patron.libraryCode == libraryCode)
                    {
                        patronList.Add(patron);
                    }
                }


                return (int)result.ResultCount;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }

        }


        public int GetPatronsByName(LoginInfo loginInfo,
            string libId,
            string libraryCode,
            string patronName,
            out List<Patron> patronList,
            out string strError)
        {
            strError = "";
            patronList = new List<Patron>();
            if (libraryCode == null)
                libraryCode = "";

            // 根据id找到图书馆对象
            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            // 2021/8/2 注释掉，改成从传进来的loginInfo
            // 使用代理账号
            //LoginInfo loginInfo = new LoginInfo("", false);

            // 从远程dp2library中查
            //string strWord = WxUserDatabase.C_PatronState_TodoReview;  // 待审核
            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SearchRequest request = new SearchRequest(id,
                loginInfo,
                "searchPatron",
                "<全部>",  //todo 这里后面考虑是否指定总分馆对应的数据库
                patronName,
                "姓名",  //状态 途径
                "exact",//"left",
                "patrons",
                "id,xml",
                1000,
                0,
                WeiXinConst.C_Search_MaxCount);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                SearchResult result = connection.SearchTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.ResultCount == -1)
                {
                    strError = "图书馆 " + lib.libName + "检索读者出错:" + result.ErrorInfo;
                    return -1;
                }
                if (result.ResultCount == 0)
                    return 0;
                foreach (Record record in result.Records)// int i = 0; i < result.ResultCount; i++)
                {
                    string patronXml = record.Data;

                    Patron patron = this.ParsePatronXml(libId,
                         patronXml,
                         this.GetPurePath(record.RecPath),
                         0,
                         false);

                    // 2022/7/22  改进在patron结构里增加了在借集合，ParsePatronXml()函数会赋值好，这里就不需要处理了。
                    /*
                    if (patron.libraryCode == libraryCode 
                        && patron.state != WxUserDatabase.C_PatronState_TodoReview)
                    {
                        string strWarningText = "";
                        string maxBorrowCountString = "";
                        string curBorrowCountString = "";
                        List<BorrowInfo2> borrowList = this.GetBorrowInfo(patronXml,
                            out strWarningText,
                            out maxBorrowCountString,
                            out curBorrowCountString);

                        Patron patronInfo = new PatronInfo();
                        patronInfo.patron = patron;
                        patronInfo.borrowList = borrowList;
                        patronList.Add(patronInfo);
                    }
                    */
                }


                return (int)result.ResultCount;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }

        }

        /// <summary>
        /// 找回密码
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="strLibraryCode"></param>
        /// <param name="name"></param>
        /// <param name="tel"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int ResetPassword(//string weixinId,
            string libId,
            //string libraryCode,
            string name,
            string tel,
            out string patronBarcode,
            out string strError)
        {
            strError = "";
            patronBarcode = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            string resultXml = "";
            string patronParam = "style=,"  //2022/8 将style里的returnMessage这个参数去掉了，依靠dp2library的发送短信功能，dp2libraray也有两种方式：一是通过MSMQ和dp2capo到公众号转发（常用），二是依据自身配置的短信接口。如果是没有安装dp2capo则没法发短信，不过如果使用公众号模块，都会安装dp2capo，所以这里就不用返回消息了。
                + "queryword=NB:" + name + "|,"
                + "tel=" + tel + ","
                + "name=" + name;

            // 消息模块
            string strMessageTemplate1 = "%name% 您好！\n您的读者帐户验证码为%temppassword%，有效时长%period%";   //2024/12/17 为了让接收短信快一点，将“临时密码”4个字改为了验证码。并且简化文字。"%name% 您好！\n您的读者帐户(证条码号为 %barcode%)已设验证码%temppassword%，%period% 内有效"


            // 使用代理账号capo 20161024 jane
            // 使用capo帐户，是因为在未绑定的时候，读者也可以找回密码
            LoginInfo loginInfo = new LoginInfo("", false);

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            CirculationRequest request = new CirculationRequest(id,
                loginInfo,
                "resetPassword",
                patronParam,
                strMessageTemplate1,//this.textBox_circulation_item.Text,
                "",//this.textBox_circulation_style.Text,
                "",//this.textBox_circulation_patronFormatList.Text,
                "",//this.textBox_circulation_itemFormatList.Text,
                "");//this.textBox_circulation_biblioFormatList.Text);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                CirculationResult result = connection.CirculationTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 10), // 10 秒
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    //strError = "操作失败：" + result.ErrorInfo;
                    //return -1;
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;
                    //strError = this.GetFriendlyErrorInfo(result, lib.libName); //result.ErrorInfo;
                    return -1;
                }

                if (result.Value == 0)
                {
                    if (result.String == "NotFound")
                    {
                        strError = "操作失败：读者 " + name + " 在图书馆系统中未设置手机号码，请先到图书馆出纳台由工作人员帮助登记手机号码。";
                    }
                    else
                    {
                        if (result.ErrorInfo.IndexOf("不存在") != -1)
                            strError = "操作失败：读者姓名和手机号 与 图书馆系统中存储的姓名和手机号不一致。";
                        else
                            strError = "操作失败：" + result.ErrorInfo;//读者姓名和手机号 与 图书馆系统中存储的姓名和手机号不一致。";//
                    }

                    strError += "\r\n当前图书馆：" + lib.libName;
                    return 0;
                }

                resultXml = result.PatronBarcode;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }

            if (string.IsNullOrEmpty(resultXml) == false)
            {

                // 准备发送短信
                /*
                <root>
                  <patron>
                    <tel>13862157150</tel>
                    <barcode>R00001</barcode>
                    <name>任延华</name>
                    <tempPassword>586284</tempPassword>
                    <expireTime>13:24:57</expireTime>
                    <period>一小时</period>
                    <refID>63aeb890-8936-4471-bfc5-8e72d5c7fe94</refID>
                  </patron>
                </root>             
                 */
                XmlDocument dom = new XmlDocument();
                try
                {
                    dom.LoadXml(resultXml);
                }
                catch (Exception ex)
                {
                    strError = "找回密码时加载返回xml出错:" + ex.Message;
                    return -1;
                }
                XmlNode nodePatron = dom.DocumentElement.SelectSingleNode("patron");
                string strRefID = DomUtil.GetNodeText(nodePatron.SelectSingleNode("refID"));
                string strTel = DomUtil.GetNodeText(nodePatron.SelectSingleNode("tel"));

                string strBarcode = DomUtil.GetNodeText(nodePatron.SelectSingleNode("barcode"));
                patronBarcode = strBarcode;//2016-8-13 jane加，返回给前端，用于指定修改密码的账户
                string strName = DomUtil.GetNodeText(nodePatron.SelectSingleNode("name"));
                string strReaderTempPassword = DomUtil.GetNodeText(nodePatron.SelectSingleNode("tempPassword"));
                string expireTime = DomUtil.GetNodeText(nodePatron.SelectSingleNode("expireTime"));
                string period = DomUtil.GetNodeText(nodePatron.SelectSingleNode("period"));

                string strMessageTemplate = "%name% 您好！密码为 %temppassword%。一小时内有效。";
                string strMessageText = strMessageTemplate.Replace("%barcode%", strBarcode)
                    .Replace("%name%", strName)
                    .Replace("%temppassword%", strReaderTempPassword)
                    .Replace("%expiretime%", expireTime)
                    .Replace("%period%", period);

                // 发送短信
                int nRet = this.SendSMS(patronBarcode,
                    strTel,
                    strMessageText,
                    lib.libName,
                    out strError);
                if (nRet == -1)
                    return -1;

            }
            return 1;

        ERROR1:
            strError += "\r\n当前图书馆：" + lib.libName;
            return -1;
        }

        // 向手机号码发送短信
        public int SendSMS(string strPatronBarcode,
            string strTel,
            string strMessageText,
            string strLibName,
            out string strError)
        {
            strError = "";
            int nRet = 0;
            // 组织读者xml，手机号必备
            string strPatronXml = "<root><tel>" + strTel + "</tel></root>";
            MessageInterface external_interface = this.GetMessageInterface("sms");
            try
            {
                // 发送一条消息
                // parameters:
                //      strPatronBarcode    读者证条码号
                //      strPatronXml    读者记录XML字符串。如果需要除证条码号以外的某些字段来确定消息发送地址，可以从XML记录中取
                //      strMessageText  消息文字
                //      strError    [out]返回错误字符串
                // return:
                //      -1  发送失败
                //      0   没有必要发送
                //      >=1   发送成功，返回实际发送的消息条数
                nRet = external_interface.HostObj.SendMessage(strPatronBarcode,
                    strPatronXml,
                    strMessageText,
                    strLibName,
                    out strError);
                if (nRet == -1 || nRet == 0)
                    return -1;
            }
            catch (Exception ex)
            {
                strError = external_interface.Type + " 类型的外部消息接口Assembly中SendMessage()函数抛出异常: " + ex.Message;
                nRet = -1;
            }
            if (nRet == -1)
            {
                strError = "向读者 '" + strPatronBarcode + "' 发送" + external_interface.Type + " message时出错：" + strError;
                this.WriteErrorLog(strError);
                return -1;
            }

            // 2020/3/31 把发送短信记录下来
            this.WriteDebug("发送短信成功：" + strMessageText);

            return nRet;
        }


        public string Get6DigitalCode()
        {
            string vc = "";
            Random rNum = new Random();//随机生成类
            int num1 = rNum.Next(0, 9);//返回指定范围内的随机数
            int num2 = rNum.Next(0, 9);
            int num3 = rNum.Next(0, 9);
            int num4 = rNum.Next(0, 9);
            int num5 = rNum.Next(0, 9);
            int num6 = rNum.Next(0, 9);

            int[] nums = new int[6] { num1, num2, num3, num4, num5, num6 };
            for (int i = 0; i < nums.Length; i++)//循环添加四个随机生成数
            {
                vc += nums[i].ToString();
            }

            return vc;
        }

        /// <summary>
        /// 发送短信验证码
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="tel"></param>
        /// <param name="verifyCode"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public int GetVerifyCode(string libId,
            string phone,
            out string verifyCode,
            out string error)
        {
            int nRet = 0;
            error = "";
            verifyCode = "";

            verifyCode = this.Get6DigitalCode();

            string strMessageText = "您的注册验证码:" + verifyCode + "，请勿向任何人出示，以免帐号被盗，1分钟内有效。";


            // todo 正常版本打开
            //发送短信
            nRet = this.SendSMS("~temp",//patronBarcode,
               phone,
               strMessageText,
               libId,
               out error);
            if (nRet == -1)
                return -1;

            return nRet;
        }

        /// <summary>
        /// 修改密码
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="patron"></param>
        /// <param name="oldPassword"></param>
        /// <param name="newPassword"></param>
        /// <param name="strError"></param>
        /// <returns>
        /// -1  出错
        /// 0   未成功
        /// 1   成功
        /// </returns>
        public int ChangePassword(string libId,
            string patron,
            string oldPassword,
            string newPassword,
            out string strError)
        {
            strError = "";
            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                goto ERROR1;
            }

            // 注意新旧两个密码参数即便为空，也应该是 "" 而不应该是 null。
            // 所以在使用 Item 参数的时候，old 和 new 两个子参数的任何一个都不该被省略。
            // 省略子参数的用法是有意义的，但不该被用在这个修改读者密码的场合。
            string item = "old=" + oldPassword + ",new=" + newPassword;

            // 使用读者账号 20161024 jane
            LoginInfo loginInfo = new LoginInfo(patron, true);

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            CirculationRequest request = new CirculationRequest(id,
                loginInfo,
                "changePassword",
                patron,
                item,
                "",//style
                "",//patronFormatList
                "",//itemFormatList
                "");//biblioFormatList
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                CirculationResult result = connection.CirculationTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 10), // 10 秒
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    strError = "出错：" + result.ErrorInfo;
                    return -1;
                }

                if (result.Value == 0)
                {
                    strError = result.ErrorInfo;
                    return 0;
                }

                return (int)result.Value;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }


        ERROR1:
            return -1;
        }



        #endregion

        #region 绑定解绑

        //账户类型：0表示读者 1表示工作人员
        public const int C_TYPE_READER = 0;
        public const int C_TYPE_WORKER = 1;


        /// <summary>
        /// 绑定帐户
        /// </summary>
        /// <param name="libId">图书馆id</param>
        /// <param name="bindLibraryCode">用户选择的分馆</param>
        /// <param name="strPrefix">绑定方式前缀</param>
        /// <param name="strWord">关键词</param>
        /// <param name="strPassword">帐户密码</param>
        /// <param name="weixinId">weixinId，前端传来的weixinId带了@appId，如果是web来源~~guid</param>
        /// <param name="userItem">返回生成的userItem</param>
        /// <param name="strError">返回出错信息</param>
        /// <returns>
        ///  -1 出错
        /// 0 绑定成功
        /// </returns>
        public int Bind(string libId,
            string bindLibraryCode,
            string strPrefix,
            string strWord,
            string strPassword,
            string weixinId,
            out WxUserItem userItem,
            out string strError)
        {
            userItem = null;
            strError = "";
            int nRet = -1;

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            // 账户类型
            int type = C_TYPE_READER;  // 默认就读者帐户
            if (strPrefix == "UN")
                type = C_TYPE_WORKER;// 工作人员账户

            // single multiple singlestrict 
            string strStyle = "multiple"; // 默认1个帐号可以绑定多个手机

            // 2021/7/21 增加检查可信设备绑定
            // 检查一下微信数据库是否已经有一个这个帐户对应的绑定记录，
            // 如果有的话，则不允许再次绑定
            LibModel libCfg = this._areaMgr.GetLibCfg(libId, bindLibraryCode);
            if (libCfg != null && string.IsNullOrEmpty(libCfg.bindStyle) == false)
            {
                if (libCfg.bindStyle != "multiple"
                    && libCfg.bindStyle != "single"
                    && libCfg.bindStyle != "singlestrict")
                {
                    strError = lib.libName + "配置的bindStyle参数值[" + libCfg.bindStyle + "]不合法。";
                    return -1;
                }

                strStyle = libCfg.bindStyle;
            }


            //string thislibName = lib.libName;
            //// 如果传了分馆，绑定帐户表中的馆名称为分馆名称，用于显示和发通知提醒
            //if (string.IsNullOrEmpty(bindLibraryCode) == false)
            //    thislibName = bindLibraryCode;

            // 组合途径与关键字
            string strFullWord = strWord;
            if (string.IsNullOrEmpty(strPrefix) == false)
                strFullWord = strPrefix + ":" + strWord;

            // 二维码绑定
            if (strPrefix == "PQR")
                strPassword = DigitalPlatform.Text.Cryptography.GetSHA1(strFullWord);

            //string partonXml = "";

            // 绑定功能使用代理账号capo -20161024 jane
            LoginInfo loginInfo = new LoginInfo("", false);
            CancellationToken cancel_token = new CancellationToken();
            string fullWeixinId = WeiXinConst.C_WeiXinIdPrefix + weixinId;//前端传来的weixinId带了@appId //+"@"+appId;
            string id = Guid.NewGuid().ToString();
            BindPatronRequest request = new BindPatronRequest(id,
                loginInfo,
                "bind",
                strFullWord,
                strPassword,
                fullWeixinId,
                 strStyle,//2021/7/21默认为multipe，如果libcfg.xml配置了bindstyle，则依据配置的值来。 //"multiple", //20180312改回多重绑定 // 2018/3/8改为单纯绑定  // 2016-8-24 改为多重绑定，这是复杂的情况，要不没法与mongodb保持一致，比较一个微信用户绑了一位读者，另一个微信用户又绑了这名相同的读者，如果不用多重绑定，就把第一名读者冲掉了，但微信mongodb并不知道。 //single,multiple
                "xml");
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                BindPatronResult result = connection.BindPatronTaskAsync(
                     lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    strError = result.ErrorInfo;
                    if (string.IsNullOrEmpty(strError) == true)
                        strError = "帐户或密码不正确";
                    return -1;
                }


                // 返回的读者xml
                string partonXml = result.Results[0];

                // 2020//11/5 针对工行的临时方案，如果设置了状态，则不允许绑定。将来用读者库的角色来代替
                {
                    XmlDocument dom = new XmlDocument();
                    dom.LoadXml(partonXml);
                    XmlNode root = dom.DocumentElement;

                    //<state>注销</state>
                    string strState = DomUtil.GetElementText(dom.DocumentElement, "state");
                    if (string.IsNullOrEmpty(strState) == false)
                    {
                        string barcode = DomUtil.GetElementText(dom.DocumentElement, "barcode");
                        // 调解绑
                        id = Guid.NewGuid().ToString();
                        request = new BindPatronRequest(id,
                            loginInfo,
                            "unbind",
                            barcode,//userItem.readerBarcode,
                            "",//password  todo
                            fullWeixinId,
                            "multiple,null_password",
                            "xml");

                        result = connection.BindPatronTaskAsync(
                            lib.capoUserName,
                           request,
                           new TimeSpan(0, 1, 0),
                           cancel_token).Result;
                        if (result.Value == -1)
                        {
                            strError = result.ErrorInfo;
                            //return -1;
                        }

                        strError = "用户状态值不支持绑定，请联系管理员。";
                        return -1;
                    }
                }


                // 把帐户信息保存到本地
                string recPath = result.RecPath;
                nRet = this.SaveUserToLocal1(weixinId,
                    libId,
                    bindLibraryCode,
                    type,
                    recPath,
                    partonXml,
                    strFullWord,
                    false,
                    out userItem,
                    out strError);
                if (nRet == -1)
                    return -1;

                /*
                // 2021/8/20废掉这个方案，因为绑定是一次性，
                // 再用读者身份登录获取一下信息意义不是太大，后面读者权限变了，用户脱敏信息也不会跟着修改。
                // 这个方案也不能处理以前绑定的信息，
                // 2021/7/29 为了实现在已绑定列表，针对读者信息脱敏，需要用读者帐号再获取一次读者信息。
                // 这个就可以按需在读者帐号中配置getreaderinfo相关级别的权限
                if (type == C_TYPE_READER)
                {
                   // 用读者信息登录，获取读者信息
                    LoginInfo readerLogin = new LoginInfo(userItem.readerBarcode, true);
                    //string timestamp = "";
                    nRet = dp2WeiXinService.Instance.GetPatronXml(libId,
                        readerLogin,
                        userItem.readerBarcode,
                        "advancexml,timestamp",  // 格式
                        out recPath,
                        out string timestamp,
                        out string patronXml,
                        out strError);
                    if (nRet == -1 || nRet == 0)
                        return -1;


                    // 取出个人信息
                    XmlDocument dom = new XmlDocument();
                    dom.LoadXml(patronXml);
                    // 证条码号
                    string strBarcode = DomUtil.GetElementText(dom.DocumentElement, "barcode");
                    // 姓名
                    string strName = DomUtil.GetElementText(dom.DocumentElement, "name");

                    userItem.readerBarcodeByReaderGet = strBarcode;
                    userItem.readerNameByReaderGet = strName;
                    WxUserDatabase.Current.Update(userItem);
                }
                */



                // 微信入口才需要发通知
                if (WxUserDatabase.CheckFrom(weixinId) == WxUserDatabase.C_from_weixin) //CheckIsFromWeb(weixinId) == false)
                {
                    string maskDef = this.GetMaskDef(libId, bindLibraryCode, out bool send2PatronIsMask);


                    // 发送绑定成功的客服消息    
                    string strFirst = "☀恭喜您！您已成功绑定图书馆读者账号。";
                    string strAccount = this.GetFullPatronName(userItem.readerName, userItem.readerBarcode, "", "", false, maskDef);//这里不需要mask
                    string strRemark = "您可以直接通过微信公众号访问图书馆，进行信息查询，预约续借等功能。如需解绑，请点击“绑定账号”菜单操作。";
                    if (type == 1)
                    {
                        strFirst = "☀恭喜您！您已成功绑定图书馆工作人员账号。";
                        strAccount = userItem.userName;
                        strRemark = "欢迎您使用微信公众号管理图书馆业务，如需解绑，请点击“绑定账号”菜单操作。";
                    }

                    string fullLibName = this.GetFullLibName(userItem.libName, userItem.libraryCode, "");
                    //string linkUrl = "";//dp2WeiXinService.Instance.OAuth2_Url_AccountIndex,//详情转到账户管理界面

                    // 本人
                    List<WxUserItem> bindPatronList = new List<WxUserItem>();
                    if (userItem != null)
                        bindPatronList.Add(userItem);
                    //string tempfullWeixinId = weixinId;//2016-11-16 传进来的weixinId带了@appId // +"@" + appId;
                    //bindWeixinIds.Add(tempfullWeixinId);

                    // 工作人员
                    List<WxUserItem> workers = this.GetTraceUsers(libId, userItem.libraryCode);

                    // 不加mask的通知数据
                    string first_color = "#000000";
                    BindTemplateData msgData = new BindTemplateData(strFirst,
                        first_color,
                        strAccount,
                        fullLibName,
                        strRemark);

                    //加mask的通知数据
                    strAccount = this.GetFullPatronName(userItem.readerName, userItem.readerBarcode, "", "", true, maskDef);//this.markString(userItem.readerName) + " " + this.markString(userItem.readerBarcode) + "";
                    if (type == 1)
                    {
                        strAccount = dp2WeiXinService.Mask(maskDef, userItem.userName, "name"); //this.markString(userItem.userName);
                    }
                    BindTemplateData maskMsgData = new BindTemplateData(strFirst,
                        first_color,
                        strAccount,
                        fullLibName,
                        strRemark);

                    // 发送微信消息
                    nRet = this.SendTemplateMsg1(GzhCfg.C_Template_Bind,
                       bindPatronList,
                       workers,
                       msgData,
                       maskMsgData,
                       this._messageLinkUrl,//linkUrl,
                       "",
                       send2PatronIsMask,
                       "",// originalXml  2023/6/7
                       out strError);
                    if (nRet == -1)
                    {
                        return -1;
                    }
                }

                // 正常返回0
                return 0;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
        }

        /// <summary>
        /// 保存帐户信息到本地数据库
        /// </summary>
        /// <param name="weixinId"></param>
        /// <param name="libId"></param>
        /// <param name="bindLibraryCode"></param>
        /// <param name="type"></param>
        /// <param name="partonXml"></param>
        /// <param name="bindFromWord"></param>
        /// <param name="userItem"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int SaveUserToLocal1(string weixinId,
            string libId,
            string bindLibraryCode,
            int type,
            string recPath,
            string partonXml,
            string bindFromWord,
            bool isRegister,
            out WxUserItem userItem,
            out string strError)
        {
            strError = "";
            long lRet = 0;
            userItem = null;

            // 统一null为"",方便后面的判断
            if (bindLibraryCode == null)
                bindLibraryCode = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }
            if (string.IsNullOrEmpty(weixinId) == true)
            {
                strError = "weixinId参数不能为空";
                return -1;
            }

            // 保存到mongodb表中的图书馆名称，
            // 如果绑定时选择的分馆，则存储分馆名称，用于显示和发通知提醒
            string thislibName = lib.libName;
            if (string.IsNullOrEmpty(bindLibraryCode) == false)
                thislibName = bindLibraryCode;

            // 读者信息
            string readerBarcode = "";
            string readerName = "";
            string refID = "";
            string department = "";
            string phone = "";
            string gender = "";
            string patronState = "";
            string noPassReason = "";

            string rights = ""; // 权限
            string location = "";

            // 工作人员帐户名
            string userName = "";

            // 读者或工作人员本身所属的分馆代码
            string libraryCode = "";

            // 工作人员账户
            if (type == C_TYPE_WORKER)
            {
                List<string> weixinIds = null;
                this.GetWorkerInfoByXml(partonXml,
                    out weixinIds,
                    out userName,
                    out libraryCode,
                    out rights);
            }
            else
            {
                List<string> weixinIds = null;
                WxUserItem patronInfo = this.GetPatronInfoByXml(partonXml, out weixinIds);
                readerBarcode = patronInfo.readerBarcode;
                readerName = patronInfo.readerName;
                refID = patronInfo.refID;
                department = patronInfo.department;
                phone = patronInfo.phone;
                gender = patronInfo.gender;
                patronState = patronInfo.patronState;
                noPassReason = patronInfo.noPassReason;
                libraryCode = patronInfo.libraryCode;
                rights = patronInfo.rights;
                location = patronInfo.location;

            }

            // 针对读者帐户，如果绑定时选择的图书馆 与 自己所属的分馆不一致，则不允许绑定
            if (type == C_TYPE_READER && bindLibraryCode != libraryCode)
            {
                strError = "您的帐户没有 " + thislibName + " 的权限,本帐户的libraryCode=[" + libraryCode + "]，准备绑定的图书馆馆代码libraryCode=[" + bindLibraryCode + "]";
                return -1;
            }


            // 针对工作人员帐号，检查绑定时选择的图书馆，是否满足该帐户的分馆管理范围
            // 原则：
            // 1) 总馆工作人员帐号可以绑定所有下级分馆
            // 2) 如果一个工作人员帐号管理多个分馆，那么绑定时选择的分馆代码应在管理的分馆范围
            // 3) 如果1）与2）均不符合，则绑定的分馆必须与帐户所属分馆一致
            if (type == C_TYPE_WORKER && bindLibraryCode != libraryCode)
            {
                // 全局帐户可以绑定全部下级分馆
                if (String.IsNullOrEmpty(bindLibraryCode) == false
                    && libraryCode == "")
                {
                    // 全局帐户，支持绑定全部下级分馆
                }
                else
                {
                    // 帐户实际管理的分馆范围
                    string[] libs = libraryCode.Split(new char[] { ',' });
                    // 绑定时选择的分馆，如果不在管理的分馆范围，则不支持绑定
                    if (libs.Contains(bindLibraryCode) == false)
                    {
                        strError = "您的帐户没有 " + thislibName + " 的权限 libraryCode=[" + libraryCode + "] bindLibraryCode=[" + bindLibraryCode + "]";
                        return -1;
                    }
                }
            }

            // 将微信id对应的public帐户都删除
            //注意这里不过滤图书馆，就是说临时选择的图书馆，如果未绑定正式帐户，则会被新选择的图书馆public帐户代替
            List<WxUserItem> publicList = WxUserDatabase.Current.GetWorkers(weixinId, "", WxUserDatabase.C_Public);
            if (publicList.Count > 0)
            {
                if (publicList.Count > 1)
                {
                    this.WriteErrorLog("!!!异常：出现" + publicList.Count + "个public帐户?应该只有一个");
                }

                for (int i = 0; i < publicList.Count; i++)
                {
                    WxUserDatabase.Current.SimpleDelete(publicList[i].id);
                }
            }

            // 从本地mongodb库检查一下是否已经绑定了本次帐户
            // 如果已经绑定，则只是更新信息，不会再创建一个帐户。
            if (type == C_TYPE_READER)
            {
                // 先找一下库中是否存在对应的读者帐户
                List<WxUserItem> list = WxUserDatabase.Current.GetPatron(weixinId, libId, readerBarcode);
                if (list != null && list.Count > 0)
                {
                    if (list.Count > 1)
                    {
                        this.WriteErrorLog("异常：找到了" + list.Count + "个读者帐号,根据weixinid=[" + weixinId + "] libId=[" + libId + "] readerBarcode=[" + readerBarcode + "],应只有1个帐户。");
                    }
                    userItem = list[0];
                }
            }
            else
            {
                List<WxUserItem> list = WxUserDatabase.Current.GetWorkers(weixinId, libId, userName);
                if (list != null && list.Count > 0)
                {
                    if (list.Count > 1)
                    {
                        this.WriteErrorLog("异常：找到" + list.Count + "个工作人员。" + "根据weixinid=[" + weixinId + "] libId=[" + libId + "] userName=[" + userName + "]");
                    }

                    userItem = list[0];
                }
            }

            // 是否给本地新增帐户
            bool bNew = false;
            if (userItem == null)
            {
                bNew = true;
                userItem = new WxUserItem();
            }

            userItem.recPath = recPath;//2020-2/28 增加recPath
            userItem.weixinId = weixinId;
            userItem.libName = thislibName;
            userItem.libId = lib.id;
            userItem.bindLibraryCode = bindLibraryCode;

            userItem.readerBarcode = readerBarcode;
            userItem.readerName = readerName;

            userItem.department = department;
            userItem.phone = phone;
            userItem.gender = gender;
            userItem.patronState = patronState;
            userItem.noPassReason = noPassReason;
            userItem.xml = partonXml;

            userItem.isRegister = isRegister;

            userItem.refID = refID;
            userItem.createTime = DateTimeUtil.DateTimeToString(DateTime.Now);
            userItem.updateTime = userItem.createTime;
            userItem.isActive = 1; // isActive是否为当前状态，读者和馆员都用这一个字段

            userItem.libraryCode = libraryCode;
            userItem.type = type;
            userItem.userName = userName;
            userItem.isActiveWorker = 0;//20220720该字段已作废。//是否是激活的工作人员账户，读者时均为0
            userItem.tracing = "off";//默认是关闭监控

            // 2017-2-14新增馆藏地
            userItem.location = location;
            userItem.selLocation = "";

            // 2017-4-19新增借不时是否校验条码
            userItem.verifyBarcode = 0;
            userItem.audioType = 4; // 2018/1/2

            // 2016-8-26 新增
            userItem.state = WxUserDatabase.C_State_Available; //1;
            userItem.remark = bindFromWord; // 当初的绑定途径和输入的关键词
            userItem.rights = rights;

            // 显示头像与封面
            userItem.showCover = 1;
            userItem.showPhoto = 1;
            userItem.bookSubject = "";

            //如果是微信来源，从微信id中拆出来公众号的appId
            string appId = "";
            int tempIndex = weixinId.IndexOf('@');
            if (tempIndex > 0)
            {
                appId = weixinId.Substring(tempIndex + 1);
            }
            userItem.appId = appId;

            // 新增 或 修改
            if (bNew == true)
            {
                WxUserDatabase.Current.Add(userItem);
                this.WriteDebug("新增帐户 " + userItem.Dump());// weixinid=" + userItem.weixinId + " id=[" + userItem.id + "]");
            }
            else
            {
                lRet = WxUserDatabase.Current.Update(userItem);
                this.WriteDebug("更新帐户 " + userItem.Dump()); //weixinid =" + userItem.weixinId + " id=[" + userItem.id + "]");
            }

            return 0;
        }


        public void WriteDebugUserInfo1(string weixinId, string lable)
        {
            string info = "";
            List<WxUserItem> l = WxUserDatabase.Current.Get(weixinId, "", -1);
            foreach (WxUserItem u in l)
            {
                info += u.id + ",";
            }
            this.WriteDebug(lable + "[" + weixinId + "]有" + l.Count + "个帐户。" + info);
        }

        // 解绑
        // isPublic是否是public帐户，20220720-ryh，整理接口时，对public帐户支持解绑删除，同时告诉上级，把actionuser设为null
        /// <returns>
        /// -1 出错
        /// 0   成功
        /// </returns>
        public int Unbind(string userId,
            out WxUserItem newActiveUser,
             out string strError,
             out bool isPublic)
        {
            isPublic = false;
            strError = "";
            newActiveUser = null;

            WxUserItem userItem = WxUserDatabase.Current.GetById(userId);
            if (userItem == null)
            {
                strError = "绑定账号未找到";
                return -1;
            }
            string weixinId = userItem.weixinId;

            // 2022/07/20 在整理接口时，觉得还是把这个限制放开，不需要卡帐号，所以绑定的都可以从本地库删除，
            // 但public不需要给调dp2library解绑接口
            if (userItem.type == WxUserDatabase.C_Type_Worker
                && userItem.userName == WxUserDatabase.C_Public)
            {
                // 2022/07/20 改为删除本地库中的记录，不返回出错了，这样也能删除public帐户
                // 删除mongodb库的记录
                WxUserDatabase.Current.Delete1(userId, out newActiveUser);
                isPublic = true;  // 2022/07/20 对public特殊处理
                return 0;

                // 2022/07/20 注释掉
                //strError = "public帐户是系统临时绑定帐号，不需要解绑";
                //return -1;
            }


            //LibEntity lib = this.GetLibById(userItem.libId);
            //2022/11/8 不能用this.GetLibById()，因为这个函数内部会过滤到期的，所以直接从底层数据库来看。
            LibEntity lib = LibDatabase.Current.GetLibById1(userItem.libId);
            if (lib == null)
            {
                strError = "未找到id为[" + userItem.libId + "]的图书馆定义。";
                return -1;
            }

            string queryWord = "";
            if (userItem.type == WxUserDatabase.C_Type_Patron)
                queryWord = userItem.readerBarcode;
            else
                queryWord = "UN:" + userItem.userName;

            // 使用代理账号capo 20161024 jane
            LoginInfo loginInfo = new LoginInfo("", false);
            string weixinIdTemp = userItem.weixinId;
            int nTemp = weixinIdTemp.IndexOf("@");
            if (nTemp > 0)
            {
                weixinIdTemp = weixinIdTemp.Substring(0, nTemp);
            }
            weixinIdTemp += "*";// 支持通配符

            // 调点对点解绑接口
            string fullWeixinId = WeiXinConst.C_WeiXinIdPrefix + weixinIdTemp;
            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            BindPatronRequest request = new BindPatronRequest(id,
                loginInfo,
                "unbind",
                queryWord,//userItem.readerBarcode,
                "",//password  绑定不需要输入密码
                fullWeixinId,
               "multiple,null_password",
                "xml");
            try
            {
                // 得到连接
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                BindPatronResult result = connection.BindPatronTaskAsync(
                     lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;
                    //strError = this.GetFriendlyErrorInfo(result, lib.libName);//result.ErrorInfo;
                    //return -1;
                }
                // 2016-11-15
                if (result.Value == 0)
                {
                    strError = "未匹配上weixinid。";
                    //return -1;
                }

                // 删除mongodb库的记录
                WxUserDatabase.Current.Delete1(userId, out newActiveUser);

                // 2022/7/20 检查来源函数增加了小程序来源，只有微信入口才发通知
                if (WxUserDatabase.CheckFrom(weixinId) == WxUserDatabase.C_from_weixin) //CheckIsFromWeb(weixinId) == false)
                {
                    string maskDef = this.GetMaskDef(lib.id, userItem.libraryCode, out bool send2PatronIsMask);
                    // 发送解绑消息    
                    string strFirst = "☀您已成功对图书馆读者账号解除绑定。";
                    string strAccount = this.GetFullPatronName(userItem.readerName, userItem.readerBarcode, "", "", false, maskDef);
                    string strRemark = "\n您现在不能查看您在该图书馆的个人信息了，如需访问，请重新绑定。";
                    if (userItem.type == WxUserDatabase.C_Type_Worker)
                    {
                        strFirst = "☀您已成功对图书馆工作人员账号解除绑定。";
                        strAccount = userItem.userName;
                        strRemark = "\n您现在不能对该图书馆进行管理工作了，如需访问，请重新绑定。";
                    }

                    string fullLibName = this.GetFullLibName(userItem.libName, userItem.libraryCode, "");

                    //string linkUrl = "";//dp2WeiXinService.Instance.OAuth2_Url_AccountIndex,//详情转到账户管理界面
                    string first_color = "#000000";

                    // 本人
                    List<WxUserItem> bindPatronList = new List<WxUserItem>();
                    bindPatronList.Add(userItem);
                    //string temp = userItem.weixinId;//2016-11-16 传进来的weixinId带了@appId //+ "@"+userItem.appId;
                    //bindWeixinIds.Add(temp);//weixinId);

                    // 打开监控功能的工作你听见
                    List<WxUserItem> workers = this.GetTraceUsers(lib.id, userItem.libraryCode);

                    //显文 
                    UnBindTemplateData msgData = new UnBindTemplateData(strFirst,
                        first_color,
                        strAccount,
                        fullLibName,
                        strRemark);

                    //mask
                    strAccount = this.GetFullPatronName(userItem.readerName, userItem.readerBarcode, "", "", true, maskDef);
                    if (userItem.type == WxUserDatabase.C_Type_Worker)
                    {
                        strAccount = dp2WeiXinService.Mask(maskDef, userItem.userName, "name");// this.markString(userItem.userName);
                    }
                    UnBindTemplateData maskMsgData = new UnBindTemplateData(strFirst,
                        first_color,
                        strAccount,
                        fullLibName,
                        strRemark);

                    int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_UnBind,
                        bindPatronList,
                        workers,
                        msgData,
                        maskMsgData,
                        this._messageLinkUrl,//linkUrl,
                        "",
                        send2PatronIsMask,
                        "",// originalXml  2023/6/7
                        out strError);
                    if (nRet == -1)
                    {
                        return -1;
                    }
                }


                // 移除该工作人员的tracing on
                if (userItem.type == WxUserDatabase.C_Type_Worker)
                {
                    userItem.tracing = "off";
                    this.UpdateTracingUser(userItem);
                }

                // 如果连dp2系统有错误信息，系统会继续解绑，但会报一个提示。
                if (String.IsNullOrEmpty(strError) == false)
                    return 1;

                return 0;


            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
                //goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
                //goto ERROR1;
            }

        }



        #endregion

        #region 检索书目


        /*
        /// <summary>
        /// 检索书目
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="strFrom"></param>
        /// <param name="strWord"></param>
        /// <returns></returns>
        public SearchBiblioResult SearchBiblio(LoginInfo loginInfo,
            string weixinId,
            string libId,
            string strFrom,
            string strWord,
            string match,
            string resultSet)
        {
            // 测试加的日志
            //dp2WeiXinService.Instance.WriteErrorLog1("走到SearchBiblio-1");

            SearchBiblioResult searchRet = new SearchBiblioResult();
            searchRet.apiResult = new ApiResult();
            searchRet.apiResult.errorCode = 0;
            searchRet.apiResult.errorInfo = "";
            searchRet.records = new List<BiblioRecord>();
            searchRet.isCanNext = false;


            // 未传入word
            if (string.IsNullOrEmpty(strWord) == true)
            {
                strWord = "";
            }


            // 未传入检索途径
            if (string.IsNullOrEmpty(strFrom) == true)
            {
                searchRet.apiResult.errorCode = -1;
                searchRet.apiResult.errorInfo = "尚未传入检索途径";
                return searchRet;
            }

            // 2021/8/2 改为外面传进来
            // 获取访问dp2library的身份
            //LoginInfo loginInfo = this.Getdp2AccoutForSearch(weixinId);



            string strError = "";
            // 这里的records是第一页的记录
            List<BiblioRecord> records = null;
            bool bNext = false;
            long lRet = this.SearchBiblioInternal(weixinId,
                libId,
                loginInfo,
                strFrom,
                strWord,
                match,
                resultSet,
                0,
                15,// 2018/3/15第一次获取15条，稍微超出平板， WeiXinConst.C_OnePage_Count,
                out records,
                out bNext,
                out strError);
            if (lRet == -1 || lRet == 0)
            {
                searchRet.apiResult.errorCode = (int)lRet;

                string libName = "";
                LibEntity libEntity = this.GetLibById(libId);
                if (libEntity != null)
                {
                    libName = libEntity.libName;
                }
                // 把帐户脱敏一下，防止在提示信息时暴露帐户。
                string maskLoginName = loginInfo.UserName;
                if (loginInfo.UserName != null && loginInfo.UserName.Length >= 1)
                    maskLoginName = loginInfo.UserName.Substring(0, 1).PadRight(loginInfo.UserName.Length, '*');
                searchRet.apiResult.errorInfo = strError + "[图书馆为" + libName + ",帐户为" + maskLoginName + "]";
                return searchRet;
            }

            searchRet.records = records;  //返回的记录集合
            searchRet.resultCount = records.Count; // 本次返回的记录数 
            searchRet.isCanNext = bNext;  //是否有下页
            searchRet.apiResult.errorCode = lRet;  //-1表示出错，0未命中，其它表示命中总数。



            return searchRet;
        }
        */

        // 2022/10/20 作废掉，逻辑比较简单，直接在外面写了，不用包装一个函数了。
        /*
        // 从结果集中获取书目记录
        public SearchBiblioResult GetBiblioFromResultSet(LoginInfo loginInfo,
            string weixinId,
            string libId,
            string resultSet,
            long start,
            long count)
        {
            SearchBiblioResult searchRet = new SearchBiblioResult();
            searchRet.apiResult = new ApiResult();
            searchRet.apiResult.errorCode = 0;
            searchRet.apiResult.errorInfo = "";
            searchRet.records = new List<BiblioRecord>();
            searchRet.isCanNext = false;

            // 2021/8/2 注释掉，改为外面传进来的loginInfo
            // 20170117,改为实际绑定的身份，如果未设置使用public
            // LoginInfo loginInfo = this.Getdp2AccoutForSearch(weixinId);


            string strError = "";
            List<BiblioRecord> records = null;
            bool bNext = false;
            long lRet = this.SearchBiblioInternal(weixinId,
                libId,
                loginInfo,
                 "",
                 "!getResult",
                 "",//match
                 resultSet,
                 start,
                 count,
                 out records,
                 out bNext,
                 out strError);
            if (lRet == -1 || lRet == 0)
            {
                searchRet.apiResult.errorCode = (int)lRet;
                searchRet.apiResult.errorInfo = strError;
                return searchRet;
            }
            searchRet.resultCount = records.Count;
            searchRet.records = records;
            searchRet.isCanNext = bNext;
            searchRet.apiResult.errorCode = lRet;
            return searchRet;
        }
        */

        // 检查是否支持对外公共检索，检索书目和检索册都使用此函数
        public bool CheckOpenSearch(string weixinId,LibEntity lib,out string error)
        {
            error = "";

            // 检查该图书馆的配置是否支持对外公开检索
            if (lib.noShareBiblio == 1)
            {
                // 检查微信用户是否绑定了图书馆账户，如果未绑定，则不能检索
                List<WxUserItem> userList = WxUserDatabase.Current.Get(weixinId, lib.id, -1);
                if (userList == null
                    || userList.Count == 0
                    || (userList.Count == 1 && userList[0].userName == WxUserDatabase.C_Public))
                {
                    error = "图书馆\"" + lib.libName + "\"不支持对外公开检索书刊。请点击'我的图书馆/绑定帐户'菜单进行绑定。";
                    return false;
                }

                // 2020/6/5 增加对注册用户的状态进行检查，待审核和审核不通过，不能检索
                if (userList.Count == 1 &&
                    (userList[0].patronState == WxUserDatabase.C_PatronState_TodoReview
                        || userList[0].patronState == WxUserDatabase.C_PatronState_NoPass)
                    )
                {
                    error = "您的帐户还未审核通过，不能检索书刊，请联系管理员。";
                    return false;
                }
            }
            return true;
        }


        public long Search(string searchType,
            string weixinId,
            string libId,
            LoginInfo loginInfo,
            string strWord,
            string strFrom,
            string match,
            string resultSet,
            long start,
            long count,
            out object records,
            out long recordsCount,
            out bool bNext,  //是否有下一页
            out string strError)
        {
            strError = "";
            //biblios = null;
            //items = null;
            records = "";
            bNext = false;
            long lRet = 0;
            recordsCount = 0;

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            // 检查该图书馆的配置是否支持对外公开检索
            bool bOpen = CheckOpenSearch(weixinId, lib, out strError);
            if (bOpen == false)
                return -1;

            if (searchType == "biblio")
            {
                List<BiblioRecord> biblios = null;
                lRet = this.SearchBiblioInternal(weixinId,
                    lib,
                    loginInfo,
                    strWord,
                    strFrom,
                    match,
                    resultSet,
                    start,
                    count,
                    out biblios,
                    out bNext,
                    out strError);

                records = biblios;
                recordsCount = biblios.Count;
                return lRet;
            }
            else if (searchType == "item")
            {
                List<BiblioItem> items = null;
                lRet = this.SearchItem(weixinId,
                    lib,
                    loginInfo,
                    strWord,
                    strFrom,
                    match,
                    resultSet,
                    start,
                    count,
                    out items,
                    out bNext,
                    out strError);

                records = items;
                recordsCount = items.Count;
                return lRet;
            }
            else
            {
                strError = "不支持的检索类型" + searchType;
                return -1;
            }

            return 0;
        }

            /// <summary>
            /// 检索书目
            /// </summary>
            /// <param name="libId"></param>
            /// <param name="strFrom"></param>
            /// <param name="strWord"></param>
            /// <param name="records">第一批的10条</param>
            /// <param name="strError"></param>
            /// <returns></returns>
           public long SearchBiblioInternal(string weixinId,
                LibEntity lib,//string libId,
                LoginInfo loginInfo,
                string strWord,
                string strFrom,
                string match,
                string resultSet,
                long start,
                long count,
                out List<BiblioRecord> records,
                out bool bNext,  //是否有下一页
                out string strError)
        {
            strError = "";
            records = new List<BiblioRecord>();
            bNext = false;

            /*
            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            // 检查该图书馆的配置是否支持对外公开检索
            bool bOpen = CheckOpenSearch(weixinId, lib, out strError);
            if (bOpen == false)
                return -1;
            */

            try
            {
                // 获取参于检索的数据库
                string dbnames = "";
                int nRet = this.GetDbNames(lib, out dbnames, out strError);
                if (nRet == -1)
                    return -1;

                // 图书馆设置的过滤条件
                string biblioFilter = lib.biblioFilter;
                if (biblioFilter == null)
                    biblioFilter = "";

                // 获取当前帐户的分馆代码
                WxUserItem activeUser = WxUserDatabase.Current.GetActive(weixinId);
                string libraryCode = "";
                if (activeUser != null)
                    libraryCode = activeUser.bindLibraryCode; // todo，这里用实际还是绑定,现在用的绑定帐户


                // 过滤条件，如果有分馆代码的过滤条件为分馆代码，如果图书馆设置的过滤条件

                // todo 2022/10/20 感觉这里应该是把两个条件组合起来，现在这种写法是排斥效果。
                // 2024/1/11 将分馆过滤与单独配置的过滤，改为组合的用法。
                string filter = "";
                if (string.IsNullOrEmpty(libraryCode) == false)
                {
                    filter = libraryCode;

                    // 2024/1/12 如果明确配置了检索全部书目，则不过滤分馆
                    LibModel libCfg = this._areaMgr.GetLibCfg(lib.id, libraryCode);
                    if (libCfg != null && string.IsNullOrEmpty(libCfg.searchAllBiblio) == false)
                    {
                        if (libCfg.searchAllBiblio.ToLower() == "true")  
                        {
                            filter = "";
                        }
                    }
                }



                // 过滤一些特殊状态的图书
                if (string.IsNullOrEmpty(biblioFilter) == false)
                {
                    if (filter != "")
                        filter += ",";

                    filter = biblioFilter;
                }

                //2022/10/27 获取的结果集按id倒序排序。
                //因为点对点的接口包括了获取结果集，所以一开始就要把排序规则传上。
                string formatList = "id,cols,sort:-0";  
                //if (strWord == "getResult")
                //{
                //    formatList += ",sort:-0";
                //}


                CancellationToken cancel_token = new CancellationToken();
                string id = Guid.NewGuid().ToString();
                SearchRequest request = new SearchRequest(id,
                    loginInfo,
                    "searchBiblio",
                    dbnames,
                    strWord,
                    strFrom,
                    match,
                    resultSet,
                    formatList,//"id,cols",
                    filter,//libraryCode,//filter 20170509
                    WeiXinConst.C_Search_MaxCount,  //最大数量 // 20190506 todo这个参数要传进来，如果不传表示用默认设置
                    start,  //每次获取范围
                    count) ;

                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                SearchResult result = connection.SearchTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.ResultCount == -1)
                {
                    bool bOffline = false;
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;

                    //strError = this.GetFriendlyErrorInfo(result, lib.libName, out bOffline);
                    if (bOffline == true)
                    {
                        // 激活工作线程，为了给工作人员发通知。
                        // todo 但发通知是超过一小时才会发通知，工作线程是10分钟一次，好像这里激活一下意义也不太大。
                        this._managerThread.Activate();
                    }
                    return -1;
                }

                if (result.ResultCount == 0)
                {
                    strError = "未命中";
                    return 0;
                }


                List<string> resultPathList = new List<string>();
                for (int i = 0; i < result.Records.Count; i++)
                {
                    string xml = result.Records[i].Data;
                    /*<root><col>请让我慢慢长大</col>
                     * <col>吴蓓著</col>
                     * <col>天津教育出版社</col>
                     * <col>2009</col>
                     * <col>G61-53</col>
                     * <col>儿童教育儿童教育</col>
                     * <col></col>
                     * <col>978-7-5309-5335-8</col></root>*/
                    XmlDocument dom = new XmlDocument();
                    dom.LoadXml(xml);
                    string name = DomUtil.GetNodeText(dom.DocumentElement.SelectSingleNode("col"));
                    string path = result.Records[i].RecPath;
                    int nIndex = path.IndexOf("@");
                    path = path.Substring(0, nIndex);

                    BiblioRecord record = new BiblioRecord();
                    record.recPath = path;
                    record.name = name;
                    record.no = (i + start + 1).ToString();//todo 注意下一页的时候
                    //record.libId = libId;
                    records.Add(record);
                }

                // 检查是否有下页
                if (start + records.Count < result.ResultCount)
                    bNext = true;

                //测试用2分钟。
                //Thread.Sleep(1000 * 60*2);

                return result.ResultCount;// records.Count;
            }
            catch (AggregateException ex)
            {
                strError = ex.Message;
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
        }


        public long SearchItem(string weixinId,
             LibEntity lib,//string libId,
             LoginInfo loginInfo,
             string strWord,
             string strFrom,
             string match,
             string resultSet,
             long start,
             long count,
             out List<BiblioItem> records,
             out bool bNext,  //是否有下一页
             out string strError)
        {
            strError = "";
            records = new List<BiblioItem>();
            bNext = false;

            try
            {
                // 获取参于检索的数据库
                string dbnames = "<全部>";

                CancellationToken cancel_token = new CancellationToken();
                string id = Guid.NewGuid().ToString();
                SearchRequest request = new SearchRequest(id,
                    loginInfo,
                    "searchItem",
                    dbnames,
                    strWord,
                    strFrom,
                    match,
                    resultSet,
                    "id,xml",
                    "",//filter todo
                    WeiXinConst.C_Search_MaxCount,  //最大数量 
                    start,  //每次获取范围
                    count);

                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                SearchResult result = connection.SearchTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.ResultCount == -1)
                {
                    bool bOffline = false;
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;

                    //strError = this.GetFriendlyErrorInfo(result, lib.libName, out bOffline);
                    if (bOffline == true)
                    {
                        // 激活工作线程，为了给工作人员发通知。
                        // todo 但发通知是超过一小时才会发通知，工作线程是10分钟一次，好像这里激活一下意义也不太大。
                        this._managerThread.Activate();
                    }
                    return -1;
                }

                if (result.ResultCount == 0)
                {
                    strError = "未命中";
                    return 0;
                }

                int tempNo = 0;

                int filterCount = 0;

                List<string> resultPathList = new List<string>();
                for (int i = 0; i < result.Records.Count; i++)
                {
                    string xml = result.Records[i].Data;
                    string path = result.Records[i].RecPath;
                    path=this.GetPurePath(path);

                    string itemDbName = "";
                    int nIndex = path.IndexOf("/");
                    if (nIndex > 0)
                        itemDbName = path.Substring(0, nIndex);

                    string biblioDbName =this.GetBiblioDbName(lib, "item", itemDbName);

                    //GetDb


                    // 需要获取一下书目库路径,要不没法删除 todo
                    /*
                     *  <itemdbgroup>
        <database name="中文图书实体" biblioDbName="中文图书" syntax="unimarc" orderDbName="中文图书订购" commentDbName="中文图书评注" inCirculation="true" role="orderRecommendStore,catalogTarget" />
                     */

                    // 解析item xml
                    BiblioItem record = this.ParseItemXml(weixinId,loginInfo, lib, xml, true);
                    if (record == null)  //内部状态过滤掉的
                    {
                        filterCount++;  //过滤数量加1
                        continue;
                    }


                    
                    record.recPath = path;
                    record.biblioPath = biblioDbName + "/" + record.parent;
                    records.Add(record);

                    // 2022/11/17 计算序号，使用的一个临时变量，不用i，因为有内部过滤到的记录
                    record.no = (tempNo + start + 1).ToString();
                    tempNo++;
                }

                // 检查是否有下页
                if (start + records.Count < result.ResultCount-filterCount)  // 2022/11/17 总命中数量要减去filterCount过滤的数量
                    bNext = true;


                return result.ResultCount-filterCount;// 2022/11/17 总命中数量要减去filterCount过滤的数量
            }
            catch (AggregateException ex)
            {
                strError = ex.Message;
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
        }

        /// <summary>
        /// 获取登录身份
        /// </summary>
        /// <param name="weixinId"></param>
        /// <returns></returns>
        public LoginInfo Getdp2AccoutForSearch1(string weixinId)
        {
            string userName = "";
            bool isPatron = false;

            if (string.IsNullOrEmpty(weixinId) == true)
            {
                return null;
            }

            WxUserItem user = WxUserDatabase.Current.GetActive(weixinId);
            if (user == null)
            {
                return null;
            }

            if (user.type == WxUserDatabase.C_Type_Patron)
            {
                isPatron = true;
                userName = user.readerBarcode;

                // 用refid登录
                userName = userName.Replace("@refid:", "RI:");
            }
            else
            {
                userName = user.userName;
                isPatron = false;
            }


            LoginInfo loginInfo = new LoginInfo(userName, isPatron);
            // 当账户为public时，注意将password设为""，不能使用null。如果密码为null，系统会用代码帐号capo模拟登录。
            if (userName == WxUserDatabase.C_Public)
                loginInfo.Password = "";

            return loginInfo;
        }


        /// <summary>
        /// 获取当前登录身份
        /// </summary>
        /// <param name="weixinId"></param>
        /// <returns></returns>
        public LoginInfo Getdp2AccoutActive(WxUserItem activeUser)
        {
            string userName = "";
            bool isPatron = false;
            if (activeUser == null)
            {
                return null;
            }

            if (activeUser.type == WxUserDatabase.C_Type_Patron)
            {
                isPatron = true;
                userName = activeUser.readerBarcode;

                // 用refid登录
                userName = userName.Replace("@refid:", "RI:");
            }
            else
            {
                userName = activeUser.userName;
                isPatron = false;
            }


            LoginInfo loginInfo = new LoginInfo(userName, isPatron);
            // 当账户为public时，注意将password设为""，不能使用null。如果密码为null，系统会用代码帐号capo模拟登录。
            if (userName == WxUserDatabase.C_Public)
                loginInfo.Password = "";

            return loginInfo;
        }


        #region 封面图像 静态函数

        public static string GetObjectHtmlFragment(string libId,
            string strBiblioRecPath,
            string objectUrl,
            string mime,
            string label)
        {
            if (string.IsNullOrEmpty(objectUrl) == true)
                return "";


            if (StringUtil.IsHttpUrl(objectUrl) == false)//StringUtil.HasHead(objectUrl, "http:") == false)
            {
                string strUri = MakeObjectUrl(strBiblioRecPath,
                      objectUrl);

                objectUrl = "../patron/GetObject?libId=" + HttpUtility.UrlEncode(libId)
                    + "&uri=" + HttpUtility.UrlEncode(strUri);
            }


            string html = "<a href='" + objectUrl + "'>" + label + "</a>";
            return html;
        }

        //        public static string GetImageHtmlFragment(string libId,
        //string strBiblioRecPath,
        //string strImageUrl,
        //    bool addOnloadEvent)
        //        {
        //            return GetImageHtmlFragment(libId,
        //                strBiblioRecPath,
        //                strImageUrl,
        //                addOnloadEvent,
        //                0);
        //        }

        public static string GetImageHtmlFragment(string libId,
            string strBiblioRecPath,
            string strImageUrl,
            bool addOnloadEvent)
        {
            if (string.IsNullOrEmpty(strImageUrl) == true)
                return "";

            if (StringUtil.IsHttpUrl(strImageUrl) == false)//StringUtil.HasHead(strImageUrl, "http:") == false)
            {
                string strUri = MakeObjectUrl(strBiblioRecPath,
                      strImageUrl);

                strImageUrl = "../patron/getphoto?libId=" + HttpUtility.UrlEncode(libId)
                + "&objectPath=" + HttpUtility.UrlEncode(strUri);
            }

            string onloadStr = "";
            if (addOnloadEvent == true)
                onloadStr = " onload='setImgSize(this)' ";

            string html = "<img src='" + strImageUrl + "'  style='max-width:200px' " + onloadStr + "></img>"; // 2016/8/19 不要人为把宽高固定了  width='100px' height='100px'
            return html;
        }

        /// <summary>
        /// 获得封面图像 URL
        /// 优先选择中等大小的图片
        /// </summary>
        /// <param name="strMARC">MARC机内格式字符串</param>
        /// <param name="strPreferredType">优先使用何种大小类型</param>
        /// <returns>返回封面图像 URL。空表示没有找到</returns>
        public static string GetCoverImageUrl(string strMARC,
            string strPreferredType = "MediumImage")
        {
            string strLargeUrl = "";
            string strMediumUrl = "";   // type:FrontCover.MediumImage
            string strUrl = ""; // type:FronCover
            string strSmallUrl = "";

            MarcRecord record = new MarcRecord(strMARC);
            MarcNodeList fields = record.select("field[@name='856']");
            foreach (MarcField field in fields)
            {
                string x = field.select("subfield[@name='x']").FirstContent;
                if (string.IsNullOrEmpty(x) == true)
                    continue;
                Hashtable table = StringUtil.ParseParameters(x, ';', ':');
                string strType = (string)table["type"];
                if (string.IsNullOrEmpty(strType) == true)
                    continue;

                string u = field.select("subfield[@name='u']").FirstContent;
                // if (string.IsNullOrEmpty(u) == true)
                //     u = field.select("subfield[@name='8']").FirstContent;

                // . 分隔 FrontCover.MediumImage
                if (StringUtil.HasHead(strType, "FrontCover." + strPreferredType) == true)
                    return u;

                if (StringUtil.HasHead(strType, "FrontCover.SmallImage") == true)
                    strSmallUrl = u;
                else if (StringUtil.HasHead(strType, "FrontCover.MediumImage") == true)
                    strMediumUrl = u;
                else if (StringUtil.HasHead(strType, "FrontCover.LargeImage") == true)
                    strLargeUrl = u;
                else if (StringUtil.HasHead(strType, "FrontCover") == true)
                    strUrl = u;

            }

            if (string.IsNullOrEmpty(strLargeUrl) == false)
                return strLargeUrl;
            if (string.IsNullOrEmpty(strMediumUrl) == false)
                return strMediumUrl;
            if (string.IsNullOrEmpty(strUrl) == false)
                return strUrl;
            return strSmallUrl;
        }

        // 为了二次开发脚本使用
        public static string MakeObjectUrl(string strRecPath,
            string strUri)
        {
            if (string.IsNullOrEmpty(strUri) == true)
                return strUri;

            if (StringUtil.IsHttpUrl(strUri) == true)//StringUtil.HasHead(strUri, "http:") == true)
                return strUri;

            if (StringUtil.HasHead(strUri, "uri:") == true)
                strUri = strUri.Substring(4).Trim();

            string strDbName = GetDbName(strRecPath);
            string strRecID = GetRecordID(strRecPath);

            string strOutputUri = "";
            ReplaceUri(strUri,
                strDbName,
                strRecID,
                out strOutputUri);

            return strOutputUri;
        }

        /// <summary>
        /// 从路径中取出库名部分
        /// </summary>
        /// <param name="strPath">路径。例如"中文图书/3"</param>
        /// <returns>返回库名部分</returns>
        public static string GetDbName(string strPath)
        {
            int nRet = strPath.LastIndexOf("/");
            if (nRet == -1)
                return strPath;

            return strPath.Substring(0, nRet).Trim();
        }

        // 
        // parammeters:
        //      strPath 路径。例如"中文图书/3"
        /// <summary>
        /// 从路径中取出记录号部分
        /// </summary>
        /// <param name="strPath">路径。例如"中文图书/3"</param>
        /// <returns>返回记录号部分</returns>
        public static string GetRecordID(string strPath)
        {
            int nRet = strPath.LastIndexOf("/");
            if (nRet == -1)
                return "";

            return strPath.Substring(nRet + 1).Trim();
        }

        static bool ReplaceUri(string strUri,
    string strCurDbName,
    string strCurRecID,
    out string strOutputUri)
        {
            strOutputUri = strUri;
            string strTemp = strUri;
            // 看看第一部分是不是object
            string strPart = GetFirstPartPath(ref strTemp);
            if (strPart == "")
                return false;

            if (strTemp == "")
            {
                strOutputUri = strCurDbName + "/" + strCurRecID + "/object/" + strPart;
                return true;
            }

            if (strPart == "object")
            {
                strOutputUri = strCurDbName + "/" + strCurRecID + "/object/" + strTemp;
                return true;
            }

            string strPart2 = GetFirstPartPath(ref strTemp);
            if (strPart2 == "")
                return false;

            if (strPart2 == "object")
            {
                strOutputUri = strCurDbName + "/" + strPart + "/object/" + strTemp;
                return false;
            }

            string strPart3 = GetFirstPartPath(ref strTemp);
            if (strPart3 == "")
                return false;

            if (strPart3 == "object")
            {
                strOutputUri = strPart + "/" + strPart2 + "/object/" + strTemp;
                return true;
            }

            return false;
        }

        // 得到strPath的第一部分,以'/'作为间隔符,同时 strPath 缩短
        public static string GetFirstPartPath(ref string strPath)
        {
            if (string.IsNullOrEmpty(strPath) == true)
                return "";

            string strResult = "";

            int nIndex = strPath.IndexOf('/');
            if (nIndex == -1)
            {
                strResult = strPath;
                strPath = "";
                return strResult;
            }

            strResult = strPath.Substring(0, nIndex);
            strPath = strPath.Substring(nIndex + 1);

            return strResult;
        }

        #endregion

        #region 根据参数直接检索item

        /*
        // 根据参数检索item
        public long SearchItem(WxUserItem userItem,
            LoginInfo loginInfo,
            string from,
            string word,
            string match,
            string cmdType,
            out List<BiblioItem> items,
            out string strError)
        {
            strError = "";
            items = new List<BiblioItem>();

            if (userItem == null)
            {
                strError = "SearchItem()的userItem参数不能为null";
                return -1;
            }


            LibEntity lib = this.GetLibById(userItem.libId);
            if (lib == null)
            {
                strError = "未找到id为[" + userItem.libId + "]的图书馆定义。";
                return -1;
            }

            //WxUserItem userItem = null;
            //List<WxUserItem> users = null;
            //if (loginInfo.UserType == "patron")
            //{
            //   users = WxUserDatabase.Current.GetPatron(weixinId, libId, loginInfo.UserName);

            //}
            //else
            //{
            //    users = WxUserDatabase.Current.Get(weixinId, libId, WxUserDatabase.C_Type_Worker, null, loginInfo.UserName, true);
            //}
            //if (users.Count == 0)
            //{
            //    strError = "没找到对应的绑定帐户";
            //    return -1;
            //}
            //userItem = users[0];

            List<BiblioRecord> biblioList = new List<BiblioRecord>();
            long lRet = this.SearchBiblioAll(userItem.weixinId,
                userItem.libId,
                loginInfo,
                from,
                word,
                match,
                out biblioList,
                out strError);
            if (lRet == -1 || lRet == 0)
                return lRet;

            string selLocation = userItem.selLocation;
            foreach (BiblioRecord biblio in biblioList)
            {
                string biblioPath = biblio.recPath;

                // 取item
                List<BiblioItem> itemList = null;
                int nRet = (int)this.GetItemInfo(userItem.weixinId,
                    lib,
                    loginInfo,
                    "",//patronBarcode
                    biblioPath,
                    cmdType,
                    out itemList,
                    out strError);
                if (nRet == -1) //0的情况表示没有册，不是错误
                {
                    return -1;
                }

                // 对这些册过滤，过滤馆藏地
                if (string.IsNullOrEmpty(selLocation) == false
                    && string.IsNullOrEmpty(cmdType) == false)
                {
                    selLocation = SubLib.ParseToSplitByComma(selLocation);
                    if (selLocation != "")
                    {
                        string[] locs = selLocation.Split(new char[] { ',' });
                        foreach (BiblioItem item in itemList)
                        {
                            //item.location有可能为 方洲小学/班级书架:1601 这种形态
                            string tempLoc = item.location;
                            int nIndex = tempLoc.IndexOf(":");
                            if (nIndex > 0)
                                tempLoc = tempLoc.Substring(0, nIndex);

                            if (locs.Contains(tempLoc) == false)
                            {
                                item.isNotCareLoc = true;
                            }
                        }
                    }
                }


                // 加到集合里
                items.AddRange(itemList);
            }

            return items.Count;
        }

        private long SearchBiblioAll(string weixinId,
            string libId,
            LoginInfo loginInfo,
            string from,
            string word,
            string match,
            out List<BiblioRecord> biblioList,
            out string strError)
        {
            strError = "";

            // 先检索书目
            string resultSet = "_searchitem";
            bool bNext = false;
            int start = 0;
            int count = WeiXinConst.C_OnePage_Count;
            biblioList = new List<BiblioRecord>();
            for (; ; )
            {
                List<BiblioRecord> records = null;
                long lRet = this.SearchBiblioInternal(weixinId,
                    libId,
                    loginInfo,
                    from,
                    word,
                    match,
                    resultSet,
                    start,
                    count,
                    out records,
                    out bNext,
                    out strError);
                if (lRet == -1)
                    return -1;
                if (lRet == 0) //本有没有数据
                {
                    break;
                }

                if (records != null && records.Count > 0)
                {
                    biblioList.AddRange(records);
                }

                if (bNext == true)
                {
                    word = "!getResult";
                    from = "";
                    match = "";
                    start += records.Count;
                }
                else
                {
                    break;
                }
            }

            return biblioList.Count();
        }
        */
        #endregion

        // 为小程序做的获取册记录接口，仅返回纯净的书目信息
        public BiblioDetailResult GetItems(LoginInfo loginInfo,
    string weixinId,
    string libId,
    string biblioPath)
        {
            BiblioDetailResult result = new BiblioDetailResult();
            result.errorCode = 0;
            result.biblioPath = biblioPath;


            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                result.errorInfo = "未找到id为[" + libId + "]的图书馆定义。";
                result.errorCode = -1;
                return result;
            }

            try
            {

                // 得到读者证条码号
                string patronBarcode = "";
                WxUserItem activeUser = WxUserDatabase.Current.GetActive(weixinId);
                if (activeUser != null && activeUser.type == WxUserDatabase.C_Type_Patron)
                {
                    patronBarcode = activeUser.readerBarcode;
                }

                // 取item
                string strError = "";
                this.WriteDebug2("开始获取items");
                List<BiblioItem> itemList = null;
                long nRet = (int)this.GetItemInfo(weixinId,
                    lib,
                    loginInfo,
                    patronBarcode,
                    biblioPath,
                    //"",
                    false,  //不获取预约详情
                    out itemList,
                    out strError);
                if (nRet == -1) //0的情况表示没有册，不是错误
                {
                    result.errorCode = -1;
                    result.errorInfo = strError;
                    return result;
                }



                result.itemList = itemList;
                result.errorCode = 1;
                return result;
            }
            catch (Exception ex)
            {
                result.errorCode = -1;
                result.errorInfo = ex.Message;
                return result;
            }


        }

        // 为小程序做的获取书目详细接口，仅返回纯净的书目信息
        public BiblioDetailResult GetBiblio(LoginInfo loginInfo,
    string weixinId,
    string libId,
    string biblioPath)
        {
            BiblioDetailResult result = new BiblioDetailResult();
            result.errorCode = 0;
            result.biblioPath = biblioPath;


            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                result.errorInfo = "未找到id为[" + libId + "]的图书馆定义。";
                result.errorCode = -1;
                return result;
            }

            //DateTime start_time = DateTime.Now;

            try
            {

                string strError = "";

                List<string> dataList = null;
                int nRet = this.GetBiblioInfo(lib,
                    loginInfo,
                    biblioPath,
                   "table:*|object_template,timestamp",
                    out dataList,
                    out strError);
                if (nRet == -1 || nRet == 0)
                {
                    result.errorCode = nRet;
                    result.errorInfo = strError;
                    return result;
                }

                string xml = dataList[0];
                XmlDocument dom = new XmlDocument();
                try
                {
                    dom.LoadXml(xml);
                }
                catch (Exception ex)
                {
                    result.errorCode = -1;
                    result.errorInfo = ex.Message;
                    return result;
                }

                string json = Newtonsoft.Json.JsonConvert.SerializeXmlNode(dom);
                result.info = json;


                //<root>
                //  <line name="_coverImage" value="http://www.hongniba.com.cn/bookclub/images/books/book_20005451_b.jpg" />
                //  <line name="题名与责任说明拼音" value="dang wo xiang shui de shi hou" type="titlepinyin" />
                //  <line name="题名与责任说明" value="当我想睡的时候 [专著]  / (美)简·R. 霍华德文 ; (美)琳内·彻丽图 ; 林芳萍翻译" type="title" />
                //  <line name="责任者" value="霍华德; 林芳萍; 彻丽" />
                //  <line name="出版发行" value="石家庄 : 河北教育出版社, 2010" />
                //  <line name="载体形态" value="1册 ; 26cm" />
                //  <line name="主题分析" value="图画故事-美国-现代" />
                //  <line name="分类号" value="中图法分类号: I712.85" />
                //  <line name="附注" value="启发精选世界优秀畅销绘本版权页英文题名：When I'm sleepy" />
                //  <line name="获得方式" value="ISBN 978-7-5434-7754-4 (精装 ) : CNY27.80" />
                //  <line name="提要文摘" value="临睡前，带着孩子一起环游世界，看看他可不可以像长颈鹿一样站着睡，和蝙蝠一起倒挂着睡，或者像企鹅一样睡在好冷好冷的地方。" />
                //<line name="数字资源" type="object">
                //        <table>
                //            <line type="" urlLabel="this is link txt" uri="1" mime="image/pjpeg" bytes="20121" />
                //        </table>
                //    </line>
                //</root>




                //    // 是否显示封面
                //    bool showCover = false;
                //    WxUserItem activeUser = WxUserDatabase.Current.GetActive(weixinId);
                //    if (activeUser != null && activeUser.showCover == 1)
                //    {
                //        showCover = true;
                //    }


                //    string strBiblioInfo = "";
                //    string imgHtml = "";// 封面图像
                //    string biblioInfo = "";

                //        nRet = this.GetTableAndImgHtml(lib,
                //            loginInfo,
                //            biblioPath,
                //            showCover,
                //            out strBiblioInfo,
                //            out imgHtml,
                //            out strError);
                //        if (nRet == -1 || nRet == 0)
                //        {
                //            result.errorCode = -1;
                //            result.errorInfo = strError;
                //            return result;
                //        }

                //        biblioInfo = "<table class='info'>"
                //                            + "<tr>"
                //                                + "<td class='biblio_info'>" + strBiblioInfo + "</td>" //image放在里面了 2016.8.8
                //                            + "</tr>"
                //                        + "</table>";

                //        /*
                //         * XmlDocument doc = new XmlDocument();
                //doc.LoadXml(xml);
                //string json = Newtonsoft.Json.JsonConvert.SerializeXmlNode(doc);

                //         */


                //result.itemList = itemList;
                result.errorCode = 1;
                return result;
            }
            catch (Exception ex)
            {
                result.errorCode = -1;
                result.errorInfo = ex.Message;
                return result;
            }


        }

        // from来源，表示是从index过来的，还是detail过来的，主要用于好书推荐的返回，后来没用注释掉了。
        // 20170116 修改使用绑定的账户，如未绑定用public
        public BiblioDetailResult GetBiblioDetail(LoginInfo loginInfo,
            string weixinId,
            string libId,
            string biblioPath,
            string format,
            string from)
        {
            BiblioDetailResult result = new BiblioDetailResult();
            result.errorCode = 0;
            result.biblioPath = biblioPath;

            string strError = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                result.errorInfo = "未找到id为[" + libId + "]的图书馆定义。";
                result.errorCode = -1;
                return result;
            }

            DateTime start_time = DateTime.Now;

            try
            {
                int nRet = 0;
                TimeSpan time_length = DateTime.Now - start_time;
                string logInfo = "";

                // 是否显示封面
                bool showCover = false;
                WxUserItem activeUser = WxUserDatabase.Current.GetActive(weixinId);
                if (activeUser != null && activeUser.showCover == 1)
                {
                    showCover = true;
                }

                // 2021/8/2 注释掉，改为使用前端传来的帐号
                // 得到登录dp2的身份
                //LoginInfo loginInfo = this.Getdp2AccoutForSearch(weixinId);

                // 取出summary
                this.WriteDebug2("开始获取biblio info");

                string strBiblioInfo = "";
                string imgHtml = "";// 封面图像
                string biblioInfo = "";
                string timestamp = "";
                if (format == "summary")
                {
                    nRet = this.GetSummaryAndImgHtml(lib,
                        loginInfo,
                       biblioPath,
                       showCover,
                       //lib,
                       out strBiblioInfo,
                       out imgHtml,
                       out strError);
                    if (nRet == -1 || nRet == 0)
                    {
                        result.errorCode = -1;
                        result.errorInfo = strError;
                        return result;
                    }

                    biblioInfo = "<table class='info'>"
                        + "<tr>"
                            + "<td class='cover'>" + imgHtml + "</td>"
                            + "<td class='biblio_info'>" + strBiblioInfo + "</td>"
                        + "</tr>"
                    + "</table>";
                }
                else if (format == "table")
                {
                    nRet = this.GetTableAndImgHtml(lib,
                        loginInfo,
                        biblioPath,
                        showCover,
                        out strBiblioInfo,
                        out imgHtml,
                        out timestamp,
                        out strError);
                    if (nRet == -1 || nRet == 0)
                    {
                        result.errorCode = -1;
                        result.errorInfo = strError;
                        return result;
                    }

                    biblioInfo = "<table class='info'>"
                                        + "<tr>"
                                            + "<td class='biblio_info'>" + strBiblioInfo + "</td>" //image放在里面了 2016.8.8
                                        + "</tr>"
                                    + "</table>";

                    /*
                     * XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);
            string json = Newtonsoft.Json.JsonConvert.SerializeXmlNode(doc);

                     */
                }
                else
                {
                    strBiblioInfo = format + "风格";
                }

                // 如果当前是工作人员帐户，出现好书推荐按钮
                string workerUserName = "";
                string recommendBtn = "";
                if (activeUser != null
                    && activeUser.type == WxUserDatabase.C_Type_Worker
                    && activeUser.userName != WxUserDatabase.C_Public)
                {
                    // 检索是否有好书推荐权限 
                    string needRight = dp2WeiXinService.C_Right_SetBook;// C_Right_SetHomePage;
                    int nHasRights = dp2WeiXinService.Instance.CheckRights(activeUser,
                        lib,
                        needRight,
                        out strError);
                    if (nHasRights == -1)
                    {
                        result.errorCode = -1;
                        result.errorInfo = strError;
                        return result;
                    }
                    // 有好书推荐的权限
                    if (nHasRights == 1)
                    {
                        workerUserName = activeUser.userName;

                        string returnUrl = "/Biblio/Index";
                        if (from == "detail")
                            returnUrl = "/Biblio/Detail?biblioPath=" + HttpUtility.UrlEncode(biblioPath);

                        string recommPath = "/Library/Book?libId=" + libId //BookEdit
                            + "&userName=" + workerUserName
                            + "&biblioPath=" + HttpUtility.UrlEncode(biblioPath)
                            + "&isNew=1";
                        // + "&returnUrl=" + HttpUtility.UrlEncode(returnUrl);
                        recommendBtn = "<button class='mui-btn  mui-btn-default' "
                            + " onclick=\"gotoUrl('" + recommPath + "')\">好书推荐</button>";
                    }

                    // 工作人员都出现编辑书目和删除书目的按钮
                    {
                        // 修改书目跳转的url
                        string biblioEditUrl = "/Biblio/BiblioEdit?biblioPath=" + HttpUtility.UrlEncode(biblioPath);
                        recommendBtn += "&nbsp;&nbsp;<button class='mui-btn  mui-btn-default' "
                            + " onclick=\"gotoUrl('" + biblioEditUrl + "')\">修改书目</button>";

                        // 删除书目
                        string deletefun = "deleteBiblio('" + biblioPath + "','" + timestamp + "')";
                        recommendBtn += "&nbsp;&nbsp;<button class='mui-btn  mui-btn-default' "
                            + " onclick=\"" + deletefun + "\">删除书目</button>";
                    }

                    if (string.IsNullOrEmpty(recommendBtn) ==false)
                        recommendBtn = "<div class='btnRow'>" + recommendBtn + "</div>";
                }

                // 书目信息上方为 table+好书推荐按钮
                result.info = biblioInfo + recommendBtn; ;
                time_length = DateTime.Now - start_time;
                string info = "获取[" + biblioPath + "]的table信息完毕 time span: " + time_length.TotalSeconds.ToString() + " secs";
                this.WriteDebug2(info);


                // 得到读者证条码号
                string patronBarcode = "";
                if (activeUser!=null &&  activeUser.type == WxUserDatabase.C_Type_Patron)
                {
                    patronBarcode = activeUser.readerBarcode;
                }

                // 取item
                this.WriteDebug2("开始获取items");
                List<BiblioItem> itemList = null;
                nRet = (int)this.GetItemInfo(weixinId,
                    lib,
                    loginInfo,
                    patronBarcode,
                    biblioPath,
                    //"",
                    true,
                    out itemList,
                    out strError);
                if (nRet == -1) //0的情况表示没有册，不是错误
                {
                    result.errorCode = -1;
                    result.errorInfo = strError;
                    return result;
                }

                // 计算用了多少时间
                time_length = DateTime.Now - start_time;
                logInfo = "获取[" + biblioPath + "]的item信息完毕 time span: " + time_length.TotalSeconds.ToString() + " secs";
                this.WriteDebug2(logInfo);

                result.itemList = itemList;
                result.errorCode = 1;
                return result;
            }
            catch (Exception ex)
            {
                result.errorCode = -1;
                result.errorInfo = ex.Message;
                return result;
            }

            //ERROR1:
            //    result.errorCode = -1;
            //    result.errorInfo = strError;
            //    return result;

        }




        //得到table风格的书目信息
        private int GetTableAndImgHtml(LibEntity lib,
            LoginInfo loginInfo,
            string biblioPath,
            bool showCover,
            out string table,
            out string coverImgHtml,
            out string timestamp,  //2022/10/17增加时间戳
            out string strError)
        {
            strError = "";
            table = "";
            coverImgHtml = "";
            timestamp = "";

            List<string> dataList = null;
            int nRet = this.GetBiblioInfo(lib,
                loginInfo,
                biblioPath,
               "table:*|object_template,timestamp",
                out dataList,
                out strError);
            if (nRet == -1 || nRet == 0)
                return nRet;

            string xml = dataList[0];
            timestamp = dataList[1];//2022/10/17取出时间戳
            XmlDocument dom = new XmlDocument();
            try
            {
                dom.LoadXml(xml);
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
            XmlNode root = dom.DocumentElement;
            //<root>
            //  <line name="_coverImage" value="http://www.hongniba.com.cn/bookclub/images/books/book_20005451_b.jpg" />
            //  <line name="题名与责任说明拼音" value="dang wo xiang shui de shi hou" type="titlepinyin" />
            //  <line name="题名与责任说明" value="当我想睡的时候 [专著]  / (美)简·R. 霍华德文 ; (美)琳内·彻丽图 ; 林芳萍翻译" type="title" />
            //  <line name="责任者" value="霍华德; 林芳萍; 彻丽" />
            //  <line name="出版发行" value="石家庄 : 河北教育出版社, 2010" />
            //  <line name="载体形态" value="1册 ; 26cm" />
            //  <line name="主题分析" value="图画故事-美国-现代" />
            //  <line name="分类号" value="中图法分类号: I712.85" />
            //  <line name="附注" value="启发精选世界优秀畅销绘本版权页英文题名：When I'm sleepy" />
            //  <line name="获得方式" value="ISBN 978-7-5434-7754-4 (精装 ) : CNY27.80" />
            //  <line name="提要文摘" value="临睡前，带着孩子一起环游世界，看看他可不可以像长颈鹿一样站着睡，和蝙蝠一起倒挂着睡，或者像企鹅一样睡在好冷好冷的地方。" />
            //<line name="数字资源" type="object">
            //        <table>
            //            <line type="" urlLabel="this is link txt" uri="1" mime="image/pjpeg" bytes="20121" />
            //        </table>
            //    </line>
            //</root>
            string imgUrl = "";
            XmlNodeList lineList = root.SelectNodes("line");
            string pinyin = "";
            foreach (XmlNode node in lineList)
            {
                string name = DomUtil.GetAttr(node, "name");
                string value = DomUtil.GetAttr(node, "value");
                string type = DomUtil.GetAttr(node, "type");

                // 处理换行
                value = value.Trim();
                value = this.ConvertHtmlLine(value);

                if (name == "_coverImage")
                {
                    imgUrl = value;
                    if (showCover == true && String.IsNullOrEmpty(imgUrl) == false)
                    {
                        coverImgHtml = dp2WeiXinService.GetImageHtmlFragment(lib.id, biblioPath, imgUrl, true);
                    }

                    table += "<tr>"
                        + "<td class='name'></td>"
                        + "<td class='value'>" + coverImgHtml + "</td>"  // style='background-color:red'
                        + "</tr>";

                    //table += "<tr>"
                    //    + "<td colspan='2'>" + coverImgHtml + "</td>"
                    //    + "</tr>";

                    continue;
                }

                // 处理数字资源
                if (this.CheckContainWord(type, "object") == true)
                {
                    string tableXml = node.InnerXml;
                    if (string.IsNullOrEmpty(tableXml) == false)
                    {
                        string resHtml = "<table>";

                        XmlDocument objectDom = new XmlDocument();
                        try
                        {
                            objectDom.LoadXml(tableXml);
                        }
                        catch (Exception ex)
                        {
                            strError = "加载table格式返回数字资源信息到dom出错:" + ex.Message;

                            WriteErrorLog(strError);
                            WriteErrorLog(value);
                            return -1;
                        }
                        XmlNodeList resLineList = objectDom.SelectNodes("table/line");
                        foreach (XmlNode resLineNode in resLineList)
                        {

                            //<line type="" urlLabel="this is link txt" uri="1" mime="image/pjpeg" bytes="20121" />
                            string urlLabel = DomUtil.GetAttr(resLineNode, "urlLabel");
                            string uri = DomUtil.GetAttr(resLineNode, "uri");
                            string mime = DomUtil.GetAttr(resLineNode, "mime");
                            string bytes = DomUtil.GetAttr(resLineNode, "bytes");


                            string objectHtml = GetObjectHtmlFragment(lib.id, biblioPath, uri, mime, urlLabel);

                            resHtml += "<tr><td style='padding-bottom:5px'>" + objectHtml;

                            if (mime != "")
                                resHtml += "<br/><span style='color:gray'>媒体类型：</span>" + mime;

                            if (bytes != "")
                                resHtml += "<br/><span style='color:gray'>字节数：</span>" + bytes;

                            if (mime == @"application/pdf")
                            {
                                string objectUri = MakeObjectUrl(biblioPath, uri);
                                string strPdfUri = objectUri + "/page:1,format:jpeg,dpi:50";
                                string imgSrc = "../patron/getphoto?libId=" + HttpUtility.UrlEncode(lib.id)
                                                     + "&objectPath=" + HttpUtility.UrlEncode(strPdfUri);

                                string onClickStr = " onclick='gotoUrl(\"/Biblio/ViewPDF?libid=" + lib.id + "&uri=" + objectUri + "\")' ";
                                string pdfImgHtml = "<img src='" + imgSrc + "'  style='max-width:200px;padding:5px;background-color:#eeeeee' " + onClickStr + " onload='setImgSize(this)' ></img>";

                                resHtml += "<br/>" + pdfImgHtml;//"<div style='padding:5px;background-color:#eeeeee'>" + pdfImgHtml + "</div>";
                            }

                            resHtml += "</td></tr>";

                        }

                        resHtml += "</table>";


                        table += "<tr>"
                           + "<td class='name'>" + name + "</td>"
                           + "<td class='value' style='word-wrap:break-word;word-break:break-all;white-space:pre-wrap'>" + resHtml + "</td>"
                           + "</tr>";
                    }

                    continue;
                }

                //这个版本，为书目信息的 table 格式，增加了一个 type 属性。注意这是一个逗号间隔的字符串，
                //虽然现在还没有用到逗号。无论是中文还是英文的书目数据，题名行都有 type=title，需要用这个来识别，以把这行加粗。
                //特殊地，中文的书目数据，还可能具有题名拼音行，那么它会有 type=titlepinyin。
                //再次强调一下，type 属性的值是一个逗号间隔的字符串，因此判断 title 和 titlepinyin 的时候要用特定的解析函数，
                //否则将来数据中一旦出现逗号的时候就会出现故障。
                //  <line name="题名与责任说明拼音" value="dang wo xiang shui de shi hou" type="titlepinyin" />
                //  <line name="题名与责任说明" value="当我想睡的时候 [专著]  林芳萍翻译" type="title" />
                // 检查是不是拼音
                if (this.CheckContainWord(type, "titlepinyin") == true) // name == "题名与责任说明拼音")
                {
                    pinyin = value;
                    continue;
                }
                // 是否为标题行
                if (this.CheckContainWord(type, "title_area") == true) // 20180516改为根据title_are判断，之前用的 "title") == true) //(name == "题名与责任说明")
                {
                    // 拼音与书名合为一行
                    if (String.IsNullOrEmpty(pinyin) == false)
                    {
                        table += "<tr>"
                            + "<td class='name'>" + name + "</td>"
                            + "<td class='titlevalue'>"
                                + "<span style='color:gray'>" + pinyin + "</span><br/>"
                                + value
                            + "</td>"
                            + "</tr>";
                    }
                    else
                    {
                        table += "<tr>"
                           + "<td class='name'>" + name + "</td>"
                           + "<td class='titlevalue'>" + value + "</td>"
                           + "</tr>";
                    }
                    continue;
                }

                table += "<tr>"
                    + "<td class='name'>" + name + "</td>"
                    + "<td class='value'>" + value + "</td>"
                    + "</tr>";
            }

            if (table != "")
            {
                table += "<tr>"
                    + "<td class='name'>路径</td>"
                    + "<td class='value'>" + biblioPath + "</td>"
                    + "</tr>";

                table = "<table class='biblio_table'>" + table + "</table>";
            }

            return 1;
        }

        public bool CheckContainWord(string text, string word)
        {
            // 先将text按逗号拆分
            string[] list = text.Split(',');
            foreach (string str in list)
            {
                if (str.Trim() == word) //注意这里去掉前后空白了
                    return true;
            }

            return false;
        }


        public string ConvertHtmlLine(string text1)
        {
            // 处理换行
            string retText = text1;

            retText = retText.Replace("\r\n", "\n");
            retText = retText.Replace("\r", "\n");
            retText = retText.Replace("\n", "<br/>");

            return retText;
        }


        private int GetSummaryAndImgHtml(LibEntity lib,
            LoginInfo loginInfo,
            string biblioPath,
            bool showCover,
            out string summary,
            out string coverImgHtml,
            out string strError)
        {
            strError = "";
            summary = "";
            coverImgHtml = "";

            List<string> dataList = null;
            int nRet = this.GetBiblioInfo(lib,
                loginInfo,
                biblioPath,
               "summary,xml",
                out dataList,
                out strError);
            if (nRet == -1 || nRet == 0)
                return nRet;

            summary = dataList[0];
            summary = "<span class='summary'>" + summary + "</span>";
            string xml = dataList[1];
            if (showCover == true)
            {
                string strOutMarcSyntax = "";
                string strMARC = "";
                string strFragmentXml = "";
                // 将XML格式转换为MARC格式
                // 自动从数据记录中获得MARC语法
                nRet = MarcUtil.Xml2Marc(xml,
                    MarcUtil.Xml2MarcStyle.Warning | MarcUtil.Xml2MarcStyle.OutputFragmentXml,
                    "",
                    out strOutMarcSyntax,
                    out strMARC,
                    out strFragmentXml,
                    out strError);
                if (nRet == -1)
                {
                    strError = "XML转换到MARC记录时出错：" + strError;
                    return -1;
                }

                string strImageUrl = GetCoverImageUrl(strMARC, "MediumImage");
                coverImgHtml = dp2WeiXinService.GetImageHtmlFragment(lib.id, biblioPath, strImageUrl, false);

            }


            return 1;
        }

        public int GetBiblioInfo(LibEntity lib,
            LoginInfo loginInfo,
            string biblioPath,
            string formatList,
            out List<string> dataList,
            out string strError)
        {
            return this.GetInfo(lib,
                loginInfo,
                "getBiblioInfo",
                biblioPath,
                formatList,
                out dataList,
                out strError);
        }



        /// <summary>
        /// get info 底层api
        /// </summary>
        /// <param name="lib"></param>
        /// <param name="method"></param>
        /// <param name="queryWord"></param>
        /// <param name="formatList"></param>
        /// <param name="dataList"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int GetInfo(LibEntity lib,
            LoginInfo loginInfo,
            string method,
            string queryWord,
            string formatList,
            out List<string> dataList,
            out string strError)
        {
            strError = "";
            dataList = new List<string>();

            int redocount = 0;

        REDO1:

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SearchRequest request = new SearchRequest(id,
                loginInfo,
                method,    // getBiblioInfo
                "",
                queryWord,
                "",
                "",
                "",
                formatList,// table
                1,
                0,
                -1);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                SearchResult result = connection.SearchTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.ResultCount == -1)
                {
                    //bool bOffline = false;
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;

                    // 通道断了，重来一次
                    if (result.ErrorCode == "ChannelReleased" && redocount == 0)
                    {
                        redocount++;
                        goto REDO1;
                    }

                    //strError = this.GetFriendlyErrorInfo(result, lib.libName, out bOffline);// result.ErrorInfo;
                    return -1;
                }
                if (result.ResultCount == 0)
                {
                    strError = "未命中";
                    return 0;
                }
                if (result.Records != null && result.Records.Count > 0)
                {
                    for (int i = 0; i < result.Records.Count; i++)
                    {
                        dataList.Add(result.Records[i].Data);
                    }
                }
                return 1;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                if (redocount == 0)
                {
                    redocount++;
                    goto REDO1;
                }

                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                if (redocount == 0)
                {
                    redocount++;
                    goto REDO1;
                }

                goto ERROR1;
            }
        ERROR1:
            return -1;
        }


        /// <summary>
        /// 获取summary
        /// </summary>
        /// <param name="capoUserName"></param>
        /// <param name="word"></param>
        /// <param name="strBiblioRecPathExclude">排除的书目路径</param>
        /// <param name="summary"></param>
        /// <param name="strRecPath"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int GetBiblioSummary(LoginInfo loginInfo,
            LibEntity lib,//string capoUserName,
            string word,
            string strBiblioRecPathExclude,
            out string summary,
            out string strRecPath,
            out string strError)
        {
            summary = "";
            strError = "";
            strRecPath = "";

            // 2021/8/2 注释掉，使用前端传来的loginInfo
            // 使用代理账号capo 20161024 jane
            //LoginInfo loginInfo = new LoginInfo("", false);

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SearchRequest request = new SearchRequest(id,
                loginInfo,
                "getBiblioSummary",
                "<全部>",
                word,
                "",
                strBiblioRecPathExclude,
                "",
                "",
                1,
                0,
                -1);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;  //+ "-1"

                SearchResult result = connection.SearchTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.ResultCount == -1)
                {
                    bool bOffline = false;
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;

                    //strError = "GetBiblioSummary()出错：" + this.GetFriendlyErrorInfo(result, lib.libName, out bOffline); //result.ErrorInfo ;
                    return -1;
                }
                if (result.ResultCount == 0)
                {
                    strError = "未命中";
                    return 0;
                }

                summary = result.Records[0].Data;
                strRecPath = result.Records[0].RecPath;


                return 1;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }
        ERROR1:
            return -1;
        }

        private long GetItemInfoApi(LibEntity lib,
            LoginInfo loginInfo,
            string biblioPath,
            string dbNameList,
            string formatList,
            long maxResults,
            out List<Record> dataList,
            out string strError)
        {
            strError = "";
            dataList = new List<Record>();

            // 使用代理账号capo 20161024 jane
            //LoginInfo loginInfo = new LoginInfo(userName, isPatron);

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SearchRequest request = new SearchRequest(id,
                loginInfo,
                "getItemInfo",
                dbNameList,
                biblioPath,
                "",
                "",
                "",
                formatList,
                maxResults,
                0,
                -1);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                SearchResult result = connection.SearchTaskAsync(
                       lib.capoUserName,
                       request,
                       new TimeSpan(0, 1, 0),
                       cancel_token).Result;

                if (result.ResultCount == -1 && result.ErrorCode != "ItemDbNotDef") // 2016-8-19 过滤到未定义实体库的情况
                {
                    bool bOffline = false;
                    strError = "图书馆 " + lib.libName + " GetItemInfo()出错:" + result.ErrorInfo;

                    //strError = "GetItemInfo()出错：" + this.GetFriendlyErrorInfo(result, lib.libName, out bOffline);//result.ErrorInfo;
                    return -1;
                }
                if (result.ResultCount == 0)
                {
                    strError = "未命中";
                    return 0;
                }

                for (int i = 0; i < result.Records.Count; i++)
                {
                    //string data = result.Records[i].Data;
                    dataList.Add(result.Records[i]);
                }

                return result.Records.Count;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }


        ERROR1:
            return -1;
        }



        public int SetItem(string loginUserName,
            string libId,
            string biblioPath,
            string action,
            BiblioItem item,
            out string strError)
        {
            strError = "";

            //if (item.price == null || item.price == "")
            //    item.price = "@price";

            // 使用代理账号capo 20161024 jane
            //LoginInfo loginInfo = new LoginInfo("", false);

            // 2020/10/10 新增册、删除册都用工作人员帐号
            LoginInfo loginInfo = new LoginInfo(loginUserName, false);



            // 根据id找到图书馆对象
            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            string parent = "";

            if (action != "delete")
            {
                int nIndex = biblioPath.IndexOf("/");
                if (nIndex > 0)
                    parent = biblioPath.Substring(nIndex + 1);
                if (parent == "")
                {
                    strError = "未得到书目id。";
                    return -1;
                }
            }

            string itemXml = "";
            /*
 <root>
  <barcode>B002</barcode> 
  <location>流通库</location> 
  <bookType>普通</bookType> 
  <accessNo>I563.85/H022</accessNo> 
  <price>CNY28.80</price> 
  <batchNo>20200405</batchNo>
</root>
             */
            //2020-3-17统一用action来判断，原来的bMergeInfo好像没有用了
            if (action == C_Action_new)
            {
                // 2022/10/27 先暂时不启用这段代码，前端传的就是去掉分馆前缀的值
                //string bookT = item.bookType;
                //int nIndex = item.bookType.IndexOf("}");
                //if (nIndex != -1)
                //{
                //    bookT = item.bookType.Substring(nIndex + 1).Trim();
                //}

                itemXml = "<root>"
                    + "<parent>" + parent + "</parent>"
                       + "<barcode>" + item.barcode + "</barcode>"
                       + "<location>" + item.location + "</location> "
                       + "<bookType>" + item.bookType + "</bookType>"
                       + "<accessNo>" + item.accessNo + "</accessNo>"
                       + "<price>" + item.price + "</price>"
                       + "<batchNo>" + item.batchNo + "</batchNo>"
                       + "</root>";
            }
            else if (action == C_Action_change)
            {
                // todo
            }
            else if (action == C_Action_delete)
            {
                // todo
            }
            else
            {
                strError = "SetItemInfo()接口不能识别的action[" + action + "]";
                return -1;
            }

            // 点对点消息实体
            /*
            public class Entity
            {
                public string Action { get; set; }
                public string RefID { get; set; }
                public Record OldRecord { get; set; }
                public Record NewRecord { get; set; }
                public string Style { get; set; }
                public string ErrorInfo { get; set; }
                public string ErrorCode { get; set; }
            }

            public class Record
            {
                public string RecPath { get; set; }
                public string Format { get; set; }
                public string Data { get; set; }
                public string Timestamp { get; set; }
            }
            */
            Record newRecord = new Record();
            //newRecord.RecPath = recPath; 
            newRecord.Format = "xml";
            newRecord.Data = itemXml;

            Entity entity = new Entity();
            entity.Action = action;
            entity.NewRecord = newRecord;

            List<Entity> entities = new List<Entity>();
            entities.Add(entity);

            // 删除
            if (action == "change" || action == "delete")
            {
                Record oldRecord = new Record();
                oldRecord.RecPath = item.recPath;

                
                oldRecord.Timestamp = item.Timestamp;// 2023/4/7现在dp2端需要时间戳参数，原来的接口不需要时间戳也能删除，
                entity.OldRecord = oldRecord;
            }

            string outputXml = "";

            // 调点对点接口
            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SetInfoRequest request = new SetInfoRequest(id,
                loginInfo,
                "setItemInfo",
                biblioPath,
                entities);
            try
            {
                string connName = C_ConnPrefix_Myself + libId;   //"<myself>:";

                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    connName).Result;//2022/10/25 改为weixin_client

                SetInfoResult result = connection.SetInfoTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    strError = "图书馆 " + lib.libName + " 的保存册信息时出错:" + result.ErrorInfo;
                    return -1;
                }

                // 取出册信息，错误信息是放在具体的册对象里
                if (result.Entities.Count > 0)
                {
                    Entity e = result.Entities[0];
                    if (e.ErrorCode!= "NoError" &&  string.IsNullOrEmpty(e.ErrorInfo) == false)
                    {
                        strError = e.ErrorInfo;
                        return -1;
                    }
                    //outputRecPath = result.Entities[0].NewRecord.RecPath;
                    //outputTimestamp = result.Entities[0].NewRecord.Timestamp;
                    outputXml = result.Entities[0].NewRecord.Data;
                }

                return (int)result.Value;

            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }




        }

        // 编辑书目
        // loginUserName:使用的dp2library帐号,一般为馆员帐户
        // loginUserType：空表示馆员，如果是读者传"patron"
        // weixinId：前端用户唯一id
        // libId：图书馆id
        // biblioFields:书目对象，是一个结构，里面有路径、要编辑的字段。
        // outputBiblioPath:返回的书目路径
        // outputTimestamp:返回的书目时间戳
        public int SetBiblio(string loginUserName,
            string loginUserType,
            string weixinId,
            string libId,
           BiblioFields biblio,
           out string outputBiblioPath,
           out string outputTimestamp,
            out string strError)
        {
            // 检查传入的参数
            //strError = "loginUserName=" + loginUserName + ";"
            //    + "loginUserType=" + loginUserType + ";"
            //    + "weixinId=" + weixinId + ";"
            //    + "libId=" + libId + "\r\n"
            //    + JsonConvert.SerializeObject(biblio);

            strError = "";
            int nRet = 0;
            outputBiblioPath = "";
            outputTimestamp = "";
            string style = "";  //删除记录时，传whenChildEmpty

            // 调dp2接口时，使用的dp2帐户
            LoginInfo loginInfo = GetLoginInfo(loginUserName, loginUserType);// new LoginInfo(loginUserName, false);

            // 根据id找到图书馆对象
            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            if (biblio.Action == C_Action_new || biblio.Action == C_Action_change)
            {
                if (string.IsNullOrEmpty(biblio.Fields) == true)
                {
                    strError = "SetBiblio()传入的字段内容参数值不能为空。";
                    return -1;
                }
            
            }

            MarcRecord marcRecord = null;

            string biblioXml = "";  //书目xml
            if (biblio.Action == C_Action_new)
            {
                //头标区
                string strMarc = "";//biblio.Header;  

                //                // marc工作单格式  todo 新增需要什么的初始化
                //                string strMarc = @"?????nam0 22?????   45__
                //100  ǂa20071012d2007    ekmy0chiy1050    ea
                //1010 ǂachi
                //102  ǂaCNǂb110000";

                // 工作单到MarcRecord对象，方便用MarcQuery处理。
                marcRecord = MarcRecord.FromWorksheet(strMarc); 


            }
            else if (biblio.Action == C_Action_change)  //修改
            {
                // 先调接口把书目数据取出来
                List<string> dataList = null;
                nRet = this.GetBiblioInfo(lib,
                    loginInfo,
                    biblio.BiblioPath,
                   "xml,timestamp",  //xml和时间戳
                    out dataList,
                    out strError);
                if (nRet == -1 || nRet == 0)
                    return nRet;

                string oldbiblioXml = dataList[0];


                // xml转成MarcRecord对象
                marcRecord =MarcHelper.MarcXml2MarcRecord(oldbiblioXml, out string outMarcSyntax, out strError);

                
            }
            else if (biblio.Action == C_Action_delete)
            {
                // 书目没有下级记录时，才可以删除。
                style = "whenChildEmpty";
            }
            else
            {
                // 目前仅支持new,change,delete。先不支持其它动作
                strError = "SetBiblio()接口不能识别的action[" + biblio.Action + "]";
                return -1;
            }


            if (marcRecord != null)
            {
                // 根据接口传入的字段组合字符串设置marc对应的可编辑字段
                MarcRecord record = MarcHelper.SetFields(marcRecord, biblio.Fields);

                // 将marc转成xml
                nRet = MarcUtil.Marc2Xml(record.Text,
                   "unimarc",
                   out biblioXml,
                   out strError);
                if (nRet == -1)
                    return -1;
            }

            // 点对点消息实体
            /*
            public class Entity
            {
                public string Action { get; set; }
                public string RefID { get; set; }
                public Record OldRecord { get; set; }
                public Record NewRecord { get; set; }
                public string Style { get; set; }
                public string ErrorInfo { get; set; }
                public string ErrorCode { get; set; }
            }
            public class Record
            {
                public string RecPath { get; set; }
                public string Format { get; set; }
                public string Data { get; set; }
                public string Timestamp { get; set; }
            }
            */

            //实体集合
            List<Entity> entities = new List<Entity>();

            // 本接口目前仅支持处理一条记录
            Entity entity = new Entity();
            entity.Action = biblio.Action;
            entity.Style = style;

            //新记录
            Record newRecord = new Record();
            newRecord.RecPath = biblio.BiblioPath;
            newRecord.Format = "xml";
            newRecord.Data = biblioXml;
            entity.NewRecord = newRecord;

            //加到集合里
            entities.Add(entity);

            // 编辑和删除时，需要设置旧记录，包括旧记录的路径和时间戳
            if (biblio.Action == "change" || biblio.Action == "delete")
            {
                Record oldRecord = new Record();
                oldRecord.RecPath = biblio.BiblioPath;
                oldRecord.Timestamp = biblio.Timestamp;
                
                // 给entity设置上旧记录
                entity.OldRecord = oldRecord;
            }

            // 调点对点接口，对应dp2的setBiblioInfo接口
            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SetInfoRequest request = new SetInfoRequest(id,
                loginInfo,
                "setBiblioInfo",
                biblio.BiblioPath,
                entities);
            try
            {
                string connName = C_ConnPrefix_Myself + libId;   //"<myself>:";
                // 连接
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    connName).Result;  //注意这里connName传的标记表示使用本图书馆的weixin_xxx帐户，这样在配置权限时，把setbiblio权限配置在weixin_xxx。
                
                //发起请求
                SetInfoResult result = connection.SetInfoTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    strError = "为图书馆'" + lib.libName + "'设置书目出错:" + result.ErrorInfo;
                    return -1;
                }

                // 取出实体对角信息，错误信息是放在具体的entity对象里
                if (result.Entities.Count > 0)
                {
                    Entity e = result.Entities[0];
                    if (e.ErrorCode != "NoError" && string.IsNullOrEmpty(e.ErrorInfo) == false)
                    {
                        strError = e.ErrorInfo;
                        return -1;
                    }
                    outputBiblioPath = result.Entities[0].NewRecord.RecPath;
                    outputTimestamp = result.Entities[0].NewRecord.Timestamp;
                }

                return (int)result.Value;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
            //结束
        }

        //
        private long GetIssue(LibEntity lib,
            LoginInfo loginInfo,
            string biblioPath,
            string style,
            out string issuePath,
            out string issueXml,
            out string strError)
        {
            strError = "";
            issueXml = "";
            issuePath = "";


            List<Record> dataList = null;
            long lRet = this.GetItemInfoApi(lib,
                loginInfo,
                biblioPath,
                "issue",
                style,
                10,
                out dataList,
                out strError);
            if (lRet == -1)
                return -1;

            if (lRet == 0)
            {
                strError = "通过biblioPath=" + biblioPath + ",style=" + style + " 查期未找到。";
                return -1;
            }

            issueXml = dataList[0].Data;
            issuePath = dataList[0].RecPath;
            issuePath = GetPurePath(issuePath);

            return 1;
        }

        // patronBarcode 这里传入读者证条码，是因为有可能登录身份是工作人员，那么就无法获取读者情况了
        public long GetItemInfo(string weixinId,  //为了获取libraryCode
            LibEntity lib,
            LoginInfo loginInfo,
            string patronBarcode,
            string biblioPath,
            //string cmdType,
            bool containReservationInfo,
            out List<BiblioItem> itemList,
            out string strError)
        {
            itemList = new List<BiblioItem>();
            strError = "";

            if (lib == null)
            {
                strError = "GetItemInfo() lib参数不能为null。";
                goto ERROR1;
            }

            // 当前帐户
            WxUserItem activeItem = WxUserDatabase.Current.GetActive(weixinId);


            // 获取册的format
            string formatList = "opac";

            //2022/8/18-ryh 将这一段统一注释掉，因为了解到接口本身，针对总馆和分馆帐户不传参数时，默认按管辖范围做了过滤，已经满足要求，不需要这里再做处理。
            //总馆帐户：
            //1）不特意指定参数，获得总馆和分馆全部册记录
            //2) 指定了librarycode:xxx，仅获得指定分馆的册
            //分馆帐户：
            //1）不特意指定参数，仅获得管辖分馆的册
            //2）指定了librarycode:xxx，仅获取指定分馆的册
            //3）指定了getotherlibraryitem，获取总馆和分馆全部册记录
            /*
            string otherformat = "";

            // 得到分馆代码
            string libraryCode = "";
            if (activeItem != null)
            {
                libraryCode = activeItem.bindLibraryCode;//2022/8/18 应该用绑定的绑定 .libraryCode; //todo 这里用实际还是绑定
                if (libraryCode == null)
                    libraryCode = "";
            }

            // 2022/8/9-ryh注：我爱图书馆这边主要使用群体是读者，为了避免读者看到很多其它馆数据，而又不能实际借阅，导致生气。所以限制了本馆帐户仅能看到本分馆的册。
            // 目前针对分馆用户（包括馆员和读者）仅看到本分馆的册，是一个为了开发简单一刀切的做法，
            // 更灵活的做法是在界面做个配置选项，比如用户自己可以在参数设置里勾选一下，是仅看本馆还是也看其它馆。但这样也给维护带来负担，所以目前先不加这个选项，
            // 后面如果有实际用户确实需要看到其它馆的册，再增加此选项。
            // 目前如果想看到书目下全部分馆的册，可以不绑定分馆帐户，用匿名方式（即public)检索书目，则能看到全部分馆的册。
            if (String.IsNullOrEmpty(libraryCode) == false)
            {
                otherformat = "librarycode:" + libraryCode;
            }
            else
            {
                otherformat = "getotherlibraryitem";
            }

            if (String.IsNullOrEmpty(otherformat) == false)
                formatList += "," + otherformat;
            */


            //获取下级册信息
            List<Record> recordList = null;
            long lRet = this.GetItemInfoApi(lib,
                loginInfo,
                biblioPath,
                "entity",//dbNameList,
                formatList,
                10,//maxResults,
                out recordList,
                out strError);
            if (lRet == -1)
                goto ERROR1;
            if (lRet == 0)
                return 0;

            for (int i = 0; i < recordList.Count; i++)
            {
                Record r= recordList[i];//result.Records[i].Data;
                string xml = r.Data;
                BiblioItem item = ParseItemXml(weixinId, loginInfo, lib, xml, containReservationInfo);
                if (item == null)
                    continue;
                item.biblioPath=biblioPath;
                item.recPath = this.GetPurePath(recordList[i].RecPath);

                // 2023/4/7 要加上时间戳，否则没法编辑和删除
                item.Timestamp= r.Timestamp;


                //=====
                // 检查数据库是否为期刊库，如果是期库，取出期的照片
                string biblioDbName = "";
                string biblioId = "";
                int index = biblioPath.LastIndexOf("/");
                biblioDbName = biblioPath.Substring(0, index);
                biblioId = biblioPath.Substring(index + 1);

                Library thisLib = this.LibManager.GetLibrary(lib.id);
                DbCfg db = thisLib.GetDb(biblioDbName);
                if (db != null && String.IsNullOrEmpty(db.IssueDbName) == false)
                {
                    string totalImgs = "";


                    XmlDocument dom = new XmlDocument();
                    dom.LoadXml(xml);
                    // 封面图片
                    // 得到检索期的字符串
                    List<IssueString> issueList = dp2StringUtil.GetIssueQueryStringFromItemXml(dom);
                    if (issueList != null && issueList.Count > 0)// todo 为啥会有多项？
                    {
                        foreach (IssueString issueStr in issueList)
                        {
                            // 获取期记录
                            string style = "query:父记录+期号|" + biblioId + "|" + issueStr.Query;
                            string issueXml = "";
                            string issuePath = "";
                            long ret = this.GetIssue(lib,
                                loginInfo,
                                biblioPath,
                                style,
                                out issuePath,
                                out issueXml,
                                out strError);
                            if (ret == -1)
                            {
                                //goto ERROR1;

                                // 2017-12-7 未找到对应的期记录
                                totalImgs += "<div style='color:red'>" + strError + "</div>";
                                continue;
                            }

                            // 从期中取出图片url
                            XmlDocument issueDom = new XmlDocument();
                            issueDom.LoadXml(issueXml);
                            string imgId = dp2StringUtil.GetCoverImageIDFromIssueRecord(issueDom, "LargeImage");
                            //string issueImgUrl = issuePath + "/object/"+imgId;
                            string imgHtml = dp2WeiXinService.GetImageHtmlFragment(lib.id, issuePath, imgId, true);

                            if (totalImgs != "")
                                totalImgs += "&nbsp;";
                            totalImgs += imgHtml;
                        }
                    }

                    item.imgHtml = totalImgs;
                }

                // 加到数组里
                itemList.Add(item);
            }

            return lRet;

        ERROR1:
            return -1;
        }


        public BiblioItem ParseItemXml(string weixinId,
            LoginInfo loginInfo,
            LibEntity lib,
            string itemXml,
            bool containReservationInfo  //是否获取借书详情和预约详情
            )
        {
            // 得到当前活动帐户，如果是读者帐户，取出证条码号，方便后面比较
            string patronBarcode = "";
            WxUserItem activeUser = WxUserDatabase.Current.GetActive(weixinId);
            if (activeUser != null && activeUser.type == WxUserDatabase.C_Type_Patron)
            {
                patronBarcode = activeUser.readerBarcode;
            }


            BiblioItem item = new BiblioItem();
            item.isGray = false;

            XmlDocument dom = new XmlDocument();
            dom.LoadXml(itemXml);


            // parent
            string parent = DomUtil.GetElementText(dom.DocumentElement, "parent");
            item.parent = parent;

            // refID
            string strRefID = DomUtil.GetElementText(dom.DocumentElement, "refID");
            item.refID = strRefID;

            // barcode
            string strBarcode = DomUtil.GetElementText(dom.DocumentElement, "barcode");
            item.pureBarcode = strBarcode;
            // 册条码号,如果册条码为空，则写为@refID：refid
            string strViewBarcode = "";
            if (string.IsNullOrEmpty(strBarcode) == false)
                strViewBarcode = strBarcode;
            else
                strViewBarcode = "@refID:" + strRefID;  //"@refID:" 
            item.barcode = strViewBarcode;


            //状态
            string strState = DomUtil.GetElementText(dom.DocumentElement, "state");

            // 检查是否是成员册，更新一下显示的状态
            bool bMember = false;
            XmlNode nodeBindingParent = dom.DocumentElement.SelectSingleNode("binding/bindingParent");
            if (nodeBindingParent != null)
            {
                bMember = true;
                StringUtil.SetInList(ref strState, "已装入合订册", true);  //更改状态

                string parentRefID = DomUtil.GetAttr(nodeBindingParent, "refID");
                item.parentInfo = "@refID:" + parentRefID;
            }
            item.state = strState;

            // 是否可用，加工中 或者  成员册不能使用，要划删除线等
            bool disable = false;
            if (StringUtil.IsInList("加工中", item.state) == true
                || bMember == true)
            {
                disable = true;
            }
            item.disable = disable;


            //卷册
            item.volume = DomUtil.GetElementText(dom.DocumentElement, "volume");


            // 馆藏地
            item.location = DomUtil.GetElementText(dom.DocumentElement, "location");
            // 检查该馆藏地的册记录是否可以显示出现
            if (string.IsNullOrEmpty(lib.NoViewLocation) == false)
            {
                // todo 图书馆不显示的馆藏地
                if (lib.NoViewLocation.IndexOf(item.location) != -1)
                {
                    // 读者身份 和 public帐户，直接不显示这一册
                    if (string.IsNullOrEmpty(patronBarcode) == false
                        || activeUser.userName == WxUserDatabase.C_Public)
                    {
                        return null;
                    }
                    else
                    {
                        // 馆员身份，灰色显示
                        item.isGray = true;
                    }
                }
            }

            // 2022/10/25 分馆帐户，针对非自己馆的册，发灰显示
            if (string.IsNullOrEmpty(activeUser.libraryCode) ==false)
            {
                string tempLibCode = "";
                int index1 = item.location.IndexOf("/");
                if (index1 > 0)
                    tempLibCode = item.location.Substring(0, index1);

                //2023/4/7 修改，有的工作人员管理多个馆
                if (StringUtil.IsInList(tempLibCode, activeUser.libraryCode) == false)// != tempLibCode)
                {
                    item.isGray = true;
                    item.location += "(非本馆-不可操作)";
                }
            }

            // 2022/6/20 过滤内部册
            if (StringUtil.IsInList("内部", item.state) == true)
            {
                // 读者帐号或者public帐户不显示
                if (string.IsNullOrEmpty(patronBarcode) == false
                    || activeUser.userName == WxUserDatabase.C_Public)
                {
                    return null;
                }
                else
                {
                    // 馆员身份，灰色显示
                    item.isGray = true;
                }
            }


            // 当前位置
            item.currentLocation = DomUtil.GetElementText(dom.DocumentElement, "currentLocation");
            // 架号
            item.shelfNo = DomUtil.GetElementText(dom.DocumentElement, "shelfNo");

            // 索引号
            item.accessNo = DomUtil.GetElementText(dom.DocumentElement, "accessNo");
            // 出版日期
            item.publishTime = DomUtil.GetElementText(dom.DocumentElement, "publishTime");
            // 价格
            item.price = DomUtil.GetElementText(dom.DocumentElement, "price");
            // 注释
            item.comment = DomUtil.GetElementText(dom.DocumentElement, "comment");

            // 借阅情况
            /*
             <borrower>R00001</borrower>
<borrowerReaderType>教职工</borrowerReaderType>
<borrowerRecPath>读者/1</borrowerRecPath>
<borrowDate>Sun, 17 Apr 2016 23:57:40 +0800</borrowDate>
<borrowPeriod>31day</borrowPeriod>
<returningDate>Wed, 18 May 2016 12:00:00 +0800</returningDate>
             */
            string strBorrower1 = DomUtil.GetElementText(dom.DocumentElement, "borrower");
            string borrowDate = DateTimeUtil.ToLocalTime(DomUtil.GetElementText(dom.DocumentElement,
"borrowDate"), "yyyy/MM/dd");
            string borrowPeriod = DomUtil.GetElementText(dom.DocumentElement, "borrowPeriod");


            borrowPeriod = GetDisplayTimePeriodStringEx(borrowPeriod);
            item.borrower = strBorrower1;
            item.borrowDate = borrowDate;
            item.borrowPeriod = borrowPeriod;

            // 2022/8/9 增加一个还书日期
            string returningDate = DateTimeUtil.ToLocalTime(DomUtil.GetElementText(dom.DocumentElement,
"returningDate"), "yyyy/MM/dd"); //DomUtil.GetElementText(dom.DocumentElement, "returningDate");
            item.returningDate = returningDate;


            //todo
            //if (string.IsNullOrEmpty(cmdType) == false)//(isPatron1 == false)
            //{
            //    if (cmdType == "borrow")
            //    {
            //        if (string.IsNullOrEmpty(item.borrower) == false
            //            || String.IsNullOrEmpty(strState) == false) //状态有值是也不允许借阅
            //        {
            //            item.isGray = true;
            //        }
            //    }
            //    else if (cmdType == "return")
            //    {
            //        // 没有在借的册需要显示为灰色
            //        if (string.IsNullOrEmpty(item.borrower) == true)
            //            item.isGray = true;
            //    }
            //}



            // 成员册 不显示“在借情况”和“预约信息”
            if (bMember == false)
            {
                // 在借信息
                string strBorrowInfo = "";
                if (string.IsNullOrEmpty(item.borrower) == true)// 在架的情况
                {
                    strBorrowInfo = "在架";
                }
                else // 册记录中存在借阅者，被借出的情况
                {
                    // 2022/10/20 去掉这里的续借按钮，因为导致接口传输的数据包过多，续借动作只在我的信息二级页面进行。
                    //// 当前帐户就是借阅者本人
                    //if (patronBarcode == item.borrower)
                    //{
                    //    // 2016-8-16 无册条码的情况，用refid:id代替，但在前端界面要写为refID-，否则js没法续约
                    //    string tempBarcode = item.barcode;
                    //    if (tempBarcode.Contains("@refID:") == true)
                    //        tempBarcode = tempBarcode.Replace("@refID:", "refID-");

                    //    strBorrowInfo =
                    //        "<table style='width:100%;border:0px'>"
                    //        + "<tr>"
                    //        + "<td class='info' style='border:0px'>借阅者：" + item.borrower + "<br/>"
                    //        + "借阅日期：" + item.borrowDate + "<br/>"
                    //        + "借期：" + item.borrowPeriod + "<br/>"
                    //        + "还书日期：" + item.returningDate
                    //        + "</td>"
                    //        + "<td class='btn' style='border:0px'>"
                    //        + "<button class='mui-btn  mui-btn-default'  onclick=\"renew('" + tempBarcode + "')\">续借</button>"
                    //        + "</td>"
                    //        + "</tr>"
                    //        + "<tr><td colspan='2'><div id='renewInfo-" + tempBarcode + "'/></td></tr>"
                    //        + "</table>";
                    //}
                    //else
                    {
                        //2022/8/5改进，接口返回的信息自然会脱敏，我爱图书馆这边原样显示返回的值即可
                        string temp = item.borrower;
                        if (temp.Length > 10 && temp.Substring(0,3)=="***")
                            temp = temp.Substring(0, 10) + "...";  // 2025/01/06 是***，且超过10位，截取

                        strBorrowInfo = "借阅者：" + temp + "<br/>" //2025/01/06 item.borrower   //"借阅者：***<br/>"
                            + "借阅日期：" + item.borrowDate + "<br/>"
                            + "借期：" + item.borrowPeriod + "<br/>"
                            + "还书日期：" + item.returningDate;
                    }
                }
                // 设置在借还是在架信息
                item.borrowInfo = strBorrowInfo;


                //=================
                // 预约信息
                if (containReservationInfo == true)  //当详情格式时，才显示预约信息。
                {
                    string reservationInfo = "";
                    bool bCanReservation = true;  // 默认是可以预约的
                    if (lib.ReserveScope == LibDatabase.C_ReserveScope_No)
                    {
                        reservationInfo = "本图书馆不支持预约图书。";
                        bCanReservation = false;
                    }
                    else if (lib.ReserveScope == LibDatabase.C_ReserveScope_All)
                    {
                        if (string.IsNullOrEmpty(patronBarcode) == false && item.borrower == patronBarcode)
                        {
                            reservationInfo = "该册目前是您在借中，不能预约。";
                            bCanReservation = false;
                        }
                    }
                    else if (lib.ReserveScope == LibDatabase.C_ReserveScope_OnlyBorrow)
                    {
                        if (string.IsNullOrEmpty(item.borrower) == true)
                        {
                            reservationInfo = "本图书馆不支持在架图书预约。";
                            bCanReservation = false;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(patronBarcode) == false && item.borrower == patronBarcode)
                            {
                                reservationInfo = "该册目前是您在借中，不能预约。";
                                bCanReservation = false;
                            }
                        }
                    }
                    else if (lib.ReserveScope == LibDatabase.C_ReserveScope_OnlyOnshelf)
                    {
                        if (string.IsNullOrEmpty(item.borrower) == false)
                        {
                            reservationInfo = "本图书馆不支持外借图书预约。";
                            bCanReservation = false;
                        }
                    }

                    // 检查该馆藏地是否预约
                    if (string.IsNullOrEmpty(lib.NoReserveLocation) == false)
                    {
                        if (lib.NoReserveLocation.IndexOf(item.location) != -1)
                        {
                            reservationInfo = "本馆藏地不支持预约。";
                            bCanReservation = false;
                        }
                    }

                    // 如果不是读者帐户，提示不是读者帐号，不能预约
                    // 如果前面已经判断是不能预约，就不走进这里，优先用前面的提示
                    if (bCanReservation == true && string.IsNullOrEmpty(patronBarcode) == true)
                    {
                        reservationInfo = "<span class='remark'>您当前帐户不是读者账号，不能预约图书，"
                            + "点击<a href='javascript:void(0)' onclick='gotoUrl(\"/Account/Index?returnUrl="
                            + HttpUtility.UrlEncode("/Biblio/Index") + "\")'>这里</a>绑定读者帐号。</span>";
                        bCanReservation = false;
                    }

                    if (disable == true)
                    {
                        reservationInfo = "图书状态不符合预约要求。";
                        bCanReservation = false;
                    }

                    // 预约信息
                    if (bCanReservation == true)
                    {
                        // 得到当前读者已经预约的信息
                        List<ReservationInfo> reserList = null;
                        if (string.IsNullOrEmpty(patronBarcode) == false)// patronBarcode != null && patronBarcode != "")
                        {
                            // 得到读者预约过哪些图书
                            reservationInfo = "";
                            int nRet = this.GetPatronReservation(lib.id,
                                loginInfo,
                                patronBarcode,
                                out reserList,
                                out string strError);
                            if (nRet == -1 || nRet == 0)
                            {
                                reservationInfo = "获取读者的预约信息时出错：" + strError;
                                goto END1;
                            }
                        }
                        string state = this.getReservationState(reserList, item.barcode);
                        reservationInfo = getReservationHtml(state, item.barcode, false, true);
                    }

                END1:
                    // 设置预约信息
                    item.reservationInfo = reservationInfo;
                }
                // ===预约信息结束===

            }

            return item;
        }

        // 得到预约状态
        private string getReservationState(List<ReservationInfo> list, string barcode)
        {
            if (list == null || list.Count == 0)
                return "未预约";
            for (int i = 0; i < list.Count; i++)
            {
                ReservationInfo reservation = list[i];
                string barcodeList = reservation.pureBarcodes;
                string arrivedBarcode = reservation.arrivedBarcode;

                //int nIndex = barcodeList.IndexOf(barcode);
                //if (nIndex >= 0)
                if (StringUtil.IsInList(barcode, barcodeList) == true)
                {
                    if (barcode == arrivedBarcode)
                        return "已到书";
                    else
                        return "已预约";
                }
            }

            return "未预约";
        }

        // 得到预约状态和操作按钮
        private string getReservationHtml(string reservationState,
            string barcode,
            bool bOnlyReserRow,
            bool showBtn)//List<ReservationInfo> list, string barcode)
        {

            // 2016-8-16 修改isbn不能预约的情况
            if (barcode.Contains("@refID:") == true)
                barcode = barcode.Replace("@refID:", "refID-");

            string html = "";
            //string reservationState = this.getReservationState(list, barcode);
            string btn = "";

            if (showBtn == true)
            {
                if (reservationState == "未预约")
                {
                    btn = "<button class='mui-btn  mui-btn-default' onclick=\"reservation(this,'" + barcode + "','new')\">预约</button>";
                }
                else if (reservationState == "已预约")
                {
                    btn = "<button class='mui-btn  mui-btn-default'  onclick=\"reservation(this,'" + barcode + "','delete')\">取消预约</button>";
                }
                else if (reservationState == "已到书")
                {
                    btn = "<button class='mui-btn  mui-btn-default'  onclick=\"reservation(this,'" + barcode + "','delete')\">放弃取书</button>";
                }
            }

            if (reservationState != "")
            {
                if (bOnlyReserRow == false)
                {
                    html += "<table style='width:100%;border:0px'>";
                }

                html += "<tr class='reserRow'>"
                    + "<td class='info'  style='border:0px'>" + reservationState + "</td><td class='btn'>" + btn + "</td>"
                    + "</tr>";

                if (bOnlyReserRow == false)
                {
                    html += "<tr>"
                        + "<td colspan='2' style='border:0px'><div class='resultInfo'></div></td>"
                    + "</tr>"
                    + "</table>";
                }
            }
            return html;
        }

        // 获取多个item的summary
        public string GetBarcodesSummary(LoginInfo loginInfo,
            LibEntity lib,//string capoUserName,
            string strBarcodes)
        {
            string strSummary = "";
            string strArrivedItemBarcode = "";
            int nIndex = strBarcodes.IndexOf("*");
            if (nIndex > 0)
            {
                string tempBarcodes = strBarcodes.Substring(0, nIndex);
                strArrivedItemBarcode = strBarcodes.Substring(nIndex + 1);
                strBarcodes = tempBarcodes;
            }

            string strDisableClass = "";
            if (string.IsNullOrEmpty(strArrivedItemBarcode) == false)
                strDisableClass = "deleted";

            string strPrevBiblioRecPath = "";
            string[] barcodes = strBarcodes.Split(new char[] { ',' });
            for (int j = 0; j < barcodes.Length; j++)
            {
                string strBarcode = barcodes[j];
                if (String.IsNullOrEmpty(strBarcode) == true)
                    continue;

                // 获得摘要
                string strOneSummary = "";
                string strBiblioRecPath = "";


                //    GetBiblioSummaryResponse result = channel.GetBiblioSummary(strBarcode, strPrevBiblioRecPath);
                string strError = "";
                int nRet = this.GetBiblioSummary(loginInfo,
                    lib,//capoUserName,
                    strBarcode,
                    strPrevBiblioRecPath,
                    out strOneSummary,
                    out strBiblioRecPath,
                    out strError);
                if (nRet == -1 || nRet == 0)
                    strOneSummary = strError;

                int tempIndex = strBiblioRecPath.IndexOf("@");
                if (tempIndex > 0)
                    strBiblioRecPath = strBiblioRecPath.Substring(0, tempIndex);

                if (strOneSummary == "" && strPrevBiblioRecPath == strBiblioRecPath)
                {
                    strOneSummary = "(同上)";
                }

                string strClass = "";
                if (string.IsNullOrEmpty(strDisableClass) == false && strBarcode != strArrivedItemBarcode)
                    strClass = " class='" + strDisableClass + "' ";

                //string strBarcodeLink = "<a " + strClass
                //    + " href='" + this.opacUrl + "/book.aspx?barcode=" + strBarcode + "'   target='_blank' " + ">" + strBarcode + "</a>";

                string strBarcodeLink = strBarcode;
                strSummary += "<div>" + strBarcodeLink + strOneSummary + "</div>";

                //var strOneSummaryOverflow = "<div style='width:100%;white-space:nowrap;overflow:hidden; text-overflow:ellipsis;'  title='" + strOneSummary + "'>"
                //   + strOneSummary
                //   + "</div>";

                //strSummary += "<table style='width:100%;table-layout:fixed;'>"
                //    + "<tr>"
                //    + "<td width='10%;vertical-align:middle'>" + strBarcodeLink + "</td>"
                //    + "<td>" + strOneSummaryOverflow + "</td>"
                //    + "</tr></table>";

                strPrevBiblioRecPath = strBiblioRecPath;
            }
            return strSummary;
        }


        #endregion

        #region 个人信息

#if no
        public int GetPatronInfo(string libId,
             string userName,
             string patronBarcode,
             string format,  //advancexml
             out Patron patron,
             out string recPath,
            out string timestamp,
            out string strError)
        {
            patron = null;
            strError = "";
            recPath = "";
            timestamp = "";

            // 获取当前账户的信息
            string strXml = "";
            int nRet = dp2WeiXinService.Instance.GetPatronXml(libId,
                userName,
                patronBarcode,
                format, //"advancexml",
                out recPath,
                out timestamp,
                out strXml,
                out strError);
            if (nRet == -1 || nRet == 0)
                return nRet;

            int showPhoto = 0;
            patron = this.ParsePatronXml(libId,
                strXml,
                recPath,
                showPhoto);



            return 1;
        }



        public int GetPatronInfo(string libId,
            string userName,
            bool isPatron,
            string patronBarcode,
            string style,
            string format,  //advancexml
            out Patron patron,
            out string info,
            out string strError)
        {
            patron = null;
            strError = "";
            info = "";

            // 获取当前账户的信息
            string strXml = "";
            string recPath = "";
            int nRet = dp2WeiXinService.Instance.GetPatronXml(libId,
                userName,
                isPatron,
                patronBarcode,
                format, //"advancexml",
                out recPath,
                out strXml,
                out strError);
            if (nRet == -1 || nRet == 0)
                return nRet;

            int showPhoto = 0;
            if (StringUtil.IsInList("img", style) == true)
                showPhoto = 1;
            patron = this.ParsePatronXml(libId,
                strXml,
                recPath,
                showPhoto);

            if (StringUtil.IsInList("html", style) == true)
            {
                info = this.GetPatronHtml(patron);
            }
            else if (StringUtil.IsInList("summary", style) == true)
            {
                info = this.GetPatronSummary(patron, userName);
            }

            return 1;
        }

        // 获取读者的html
        public string GetPatronHtml(Patron patron, bool showQrcode = true)
        {
            string html = "";

            html = "<table class='personalinfo'>"
            + "<tr class='person_name'> <td class='name'>姓名</td> <td class='value'>" + patron.name + "</td> </tr>"
            + "<tr> <td class='name'>性别</td> <td class='value'>" + patron.gender + "</td> </tr>"
            + "<tr> <td class='name'>出生日期</td> <td class='value'>" + patron.dateOfBirth + "</td> </tr>"
            + "<tr> <td class='name'>证号</td> <td class='value'>" + patron.cardNumber + "</td> </tr>"
            + "<tr> <td class='name'>身份证号</td> <td class='value'>" + patron.idCardNumber + "</td> </tr>"
            + "<tr> <td class='name'>单位</td> <td class='value'>" + patron.department + "</td> </tr>"
            + "<tr> <td class='name'>职务</td> <td class='value'>" + patron.post + "</td> </tr>"
            + "<tr> <td class='name'>地址</td> <td class='value'>" + patron.address + "</td> </tr>"
            + "<tr> <td class='name'>电话</td> <td class='value'>" + patron.tel + "</td> </tr>"
            + "<tr> <td class='name'>Email</td> <td class='value'>" + patron.email + "</td> </tr>"
            + "<tr class='barcode'> <td class='name'>证条码号</td> <td class='value'>" + patron.barcode + "</td> </tr>"
            + "<tr> <td class='name'>读者类型</td> <td class='value'>" + patron.readerType + "</td> </tr>"
            + "<tr class='state'> <td class='name'> 证状态</td> <td class='value'>" + patron.state + "</td> </tr>"
            + "<tr class='createdate'> <td class='name'>发证日期</td> <td class='value'>" + patron.createDate + "</td> </tr>"
            + "<tr class='expiredate'> <td class='name'>证失效期</td> <td class='value'>" + patron.expireDate + "</td> </tr>"
            + "<tr class='hire'> <td class='name'>租金</td> <td class='value'>" + patron.hire + "</td> </tr>";

            if (string.IsNullOrEmpty(patron.imageUrl) == false)
            {
                html += "<tr class='photo'>"
                   + "<td class='name'>头像</td>"
                   + "<td class='value'>"
                   + "<img src='" + patron.imageUrl + "' style='max-width:200px' />"
                   + "</td>"
                   + "</tr>";
            }
            html += "</table>";

            if (showQrcode == true)
            {
                // 二维码
                html += "<div class='mui-content-padded'>"
                                + "<center>"
                                    + "<img id='qrcode' src='" + patron.qrcodeUrl + "' alt='QRCode image' />"
                                    + "<div style='font-size:9pt'>(不要把二维码展示和提供给无关人员，以免账号被窃。)</div>"
                                + "</center>"
                           + "</div>";
            }

            return html;
        }

        // 获取读者的summary
        public string GetPatronSummary(Patron patron, string userName)
        {
            string summary = "";

            //summary += "<div onclick='gotoUrl(\"/patron/PersonalInfo?loginUserName=" + HttpUtility.UrlEncode(userName) + "&patronBarcode=" + HttpUtility.UrlEncode(patron.barcode) + "\")'>";
            //summary += "<span style='font-size: 14.8px;font-weight:bold'>" + patron.barcode + "</span>"
            //    + "&nbsp;"
            //    + "<span style='font-size: 14.8px;font-weight:bold'>" + patron.name + "</span>";
            //if (String.IsNullOrEmpty(patron.department)==false)
            //    summary += "<span style='font-size: 14.8px;color:gray'>(" + patron.department + ")</span>";
            //summary += "&nbsp;&nbsp;>";
            //summary += "<div>";



            summary += "<ul class='mui-table-view'>"
    + "<li class='mui-table-view-cell'>"
            + "<a class='mui-navigate-right' href='../Patron/PersonalInfo?loginUserName=" + HttpUtility.UrlEncode(userName) + "&patronBarcode=" + HttpUtility.UrlEncode(patron.barcode) + "'>"
            + "<table class='simplepatron'>"
                + "<tbody>"
                    + "<tr>"
                        + "<td>"
                            + "<span class='barcode'>" + patron.barcode + "</span>" + "\n"
                            + "<span class='name'>" + patron.name + "</span>" + "\n";
            if (String.IsNullOrEmpty(patron.department) == false)
            {
                summary += "<span class='department'>(" + patron.department + ")</span>";
            }
            summary += "</td>"
                    + "</tr>"
                    + "<tr>"
                        + "<td>";

            if (patron.BorrowCountHtml != "")
            {
                summary += "<span>在借" + patron.BorrowCountHtml + "</span>" + "\n";
            }

            if (patron.CaoQiCount > 0)
            {
                summary += "<span class='mui-badge overdueNum'>" + patron.CaoQiCount + "</span>" + "\n"
                    + "<span class='rightText'>超期</span>" + "\n";

            }
            if (patron.OverdueCount > 0)
            {
                summary += "<span class='mui-badge amerceNum'>" + patron.OverdueCount + "</span>" + "\n"
                    + "<span class='rightText'>待交费</span>" + "\n";
            }

            //if (patron.ReservationCountHtml != "")
            //{
            //    summary += "<span>预约" + patron.ReservationCountHtml + "</span>" + "\n";
            //}

            if (patron.ArrivedCount > 0)
            {
                summary += "<span class='mui-badge arriveNum'>" + patron.ArrivedCount + "</span>" + "\n"
                + "<span class='rightText'>预约到书</span>" + "\n";
            }

            summary += "</td>"
                    + "</tr>"
                + "</tbody>"
            + "</table>"
        + "</a>"
    + "</li>"
+ "</ul>";


            return summary;
        }
#endif

        /// <summary>
        /// 获取读者的预约信息
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="patronBarcode"></param>
        /// <param name="reservations"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int GetPatronReservation(string libId,
            LoginInfo loginInfo,
            string patronBarcode,
            out List<ReservationInfo> reservations,
            out string strError)
        {
            reservations = new List<ReservationInfo>();
            strError = "";

            string xml = "";
            string recPath = "";
            string timestamp = "";
            int nRet = dp2WeiXinService.Instance.GetPatronXml(libId,
                loginInfo,
                patronBarcode,
                "xml",
                out recPath,
                out timestamp,
                out xml,
                out strError);
            if (nRet == -1)
                return -1;

            if (nRet == 0)
            {
                strError = "从dp2library未找到证条码号为'" + patronBarcode + "'的记录";
                return 0;
            }

            // 预约请求
            string strReservationWarningText = "";
            reservations = GetReservations(xml, out strReservationWarningText);

            return 1;
        }

        public Patron ParsePatronXml(string libId,
            string strPatronXml,
            string recPath,
            int showPhoto,
            bool bMaskPhone)
        {
            // 取出个人信息
            Patron patron = new Patron();
            patron.recPath = recPath;

            XmlDocument dom = new XmlDocument();
            dom.LoadXml(strPatronXml);

            // 证条码号
            string refID = DomUtil.GetElementText(dom.DocumentElement, "refID");
            patron.refID = refID;

            // 证条码号
            string strBarcode = DomUtil.GetElementText(dom.DocumentElement, "barcode");
            patron.barcode = strBarcode;
            if (string.IsNullOrEmpty(patron.barcode) == true)
            {
                patron.barcode = "@refId:" + patron.refID;
            }

            // 显示名
            string strDisplayName = DomUtil.GetElementText(dom.DocumentElement, "displayName");
            patron.displayName = strDisplayName;

            // 姓名
            string strName = DomUtil.GetElementText(dom.DocumentElement, "name");
            patron.name = strName;

            // 性别
            string strGender = DomUtil.GetElementText(dom.DocumentElement, "gender");
            patron.gender = strGender;

            // 出生日期
            string strDateOfBirth = DomUtil.GetElementText(dom.DocumentElement, "dateOfBirth");
            if (string.IsNullOrEmpty(strDateOfBirth) == true)
                strDateOfBirth = DomUtil.GetElementText(dom.DocumentElement, "birthday");
            strDateOfBirth = DateTimeUtil.LocalDate(strDateOfBirth);
            patron.dateOfBirth = strDateOfBirth;

            // 证号 2008/11/11
            string strCardNumber = DomUtil.GetElementText(dom.DocumentElement, "cardNumber");
            patron.cardNumber = strCardNumber;

            // 身份证号
            string strIdCardNumber = DomUtil.GetElementText(dom.DocumentElement, "idCardNumber");
            patron.idCardNumber = strIdCardNumber;

            // 单位
            string strDepartment = DomUtil.GetElementText(dom.DocumentElement, "department");
            patron.department = strDepartment;

            // 职务
            string strPost = DomUtil.GetElementText(dom.DocumentElement, "post");
            patron.post = strPost;

            // 地址
            string strAddress = DomUtil.GetElementText(dom.DocumentElement, "address");
            patron.address = strAddress;

            // 电话
            string strTel = DomUtil.GetElementText(dom.DocumentElement, "tel");
            patron.phone = strTel;
            // 2020/10/19 公众号上支持修改电话，所以改为不用*号，要不用户不知道以前是什么号
            //if (strTel.Length > 4 && bMaskPhone == true)
            //{
            //    string left = strTel.Substring(0, strTel.Length - 4);
            //    left = "".PadLeft(left.Length, '*');
            //    patron.phone = left + strTel.Substring(strTel.Length - 4);
            //}

            // email
            string strEmail = DomUtil.GetElementText(dom.DocumentElement, "email");
            patron.email = this.RemoveWeiXinId(strEmail);//过滤掉微信id

            // 读者类型
            string strReaderType = DomUtil.GetElementText(dom.DocumentElement, "readerType");
            patron.readerType = strReaderType;

            // 分馆代码
            string libraryCode = DomUtil.GetElementText(dom.DocumentElement, "libraryCode");
            patron.libraryCode = libraryCode;

            // 证状态
            string strState = DomUtil.GetElementText(dom.DocumentElement, "state");
            patron.state = strState;

            // 备注
            string strComment = DomUtil.GetElementText(dom.DocumentElement, "comment");
            patron.comment = strComment;

            // 发证日期
            string strCreateDate = DomUtil.GetElementText(dom.DocumentElement, "createDate");
            strCreateDate = DateTimeUtil.LocalDate(strCreateDate);
            patron.createDate = strCreateDate;

            // 证失效期
            string strExpireDate = DomUtil.GetElementText(dom.DocumentElement, "expireDate");
            strExpireDate = DateTimeUtil.LocalDate(strExpireDate);
            patron.expireDate = strExpireDate;

            // 租金 
            string strHireExpireDate = "";
            string strHirePeriod = "";
            XmlNode nodeHire = dom.DocumentElement.SelectSingleNode("hire");
            string strHire = "";
            if (nodeHire != null)
            {
                strHireExpireDate = DomUtil.GetAttr(nodeHire, "expireDate");
                strHirePeriod = DomUtil.GetAttr(nodeHire, "period");

                strHireExpireDate = DateTimeUtil.LocalDate(strHireExpireDate);
                strHirePeriod = dp2WeiXinService.GetDisplayTimePeriodStringEx(strHirePeriod);

                strHire = "周期" + ": " + strHirePeriod + "; "
                + "失效期" + ": " + strHireExpireDate;
            }
            patron.hire = strHire;

            // 押金 2008/11/11
            string strForegift = DomUtil.GetElementText(dom.DocumentElement,
                "foregift");
            patron.foregift = strForegift;

            // 二维码
            // 2022/4/26注：如果用户实例配置了读者信息脱敏，证条码可能是一个***的情况。如果有***号，索性就不显示了，调用者外面再设一下参数。
            if (patron.barcode.IndexOf("*") == -1)
            {
                string qrcodeUrl = "../patron/getphoto?libId=" + HttpUtility.UrlEncode(libId)
                    + "&type=pqri"
                    + "&barcode=" + HttpUtility.UrlEncode(patron.barcode);
                patron.qrcodeUrl = qrcodeUrl;
            }

            //头像
            string imageUrl = "";
            if (showPhoto == 1)
            {
                // 检查是不是已经有头像图片对象 usage='cardphoto'
                XmlNamespaceManager nsmgr = new XmlNamespaceManager(new NameTable());
                nsmgr.AddNamespace("dprms", DpNs.dprms);  //注意xml有命令空间dprms:file
                XmlNodeList fileNodes = dom.DocumentElement.SelectNodes("//dprms:file[@usage='cardphoto']", nsmgr);
                if (fileNodes.Count > 0)
                {
                    string strPhotoPath = recPath + "/object/" + DomUtil.GetAttr(fileNodes[0], "id");

                    imageUrl = "../patron/getphoto?libId=" + HttpUtility.UrlEncode(libId)
                    + "&objectPath=" + HttpUtility.UrlEncode(strPhotoPath);
                }
            }
            patron.imageUrl = imageUrl; //给patron对象设置头像url


            // 违约
            List<OverdueInfo> overdueLit = new List<OverdueInfo>();
            XmlNodeList nodes = dom.DocumentElement.SelectNodes("overdues/overdue");
            patron.OverdueCount = nodes.Count;
            patron.OverdueCountHtml = ConvertToString(patron.OverdueCount);

            string strWarningText = "";
            List<OverdueInfo> overdueList = dp2WeiXinService.Instance.GetOverdueInfo(strPatronXml,
                out strWarningText);
            patron.overdueList = overdueList;

            // 在借
            nodes = dom.DocumentElement.SelectNodes("borrows/borrow");
            patron.BorrowCount = nodes.Count;
            patron.BorrowCountHtml = ConvertToString(patron.BorrowCount);
            int caoQiCount = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                XmlNode node = nodes[i];
                string strIsOverdue = DomUtil.GetAttr(node, "isOverdue");
                if (strIsOverdue == "yes")
                {
                    caoQiCount++;
                }
            }
            patron.CaoQiCount = caoQiCount;

            //string strWarningText = "";
            string maxBorrowCountString = "";
            string curBorrowCountString = "";
            List<BorrowInfo2> borrowList = dp2WeiXinService.Instance.GetBorrowInfo(strPatronXml,
                out strWarningText,
                out maxBorrowCountString,
                out curBorrowCountString);
            patron.borrowList = borrowList;
            patron.maxBorrowCount = maxBorrowCountString;
            patron.curBorrowCount = curBorrowCountString;

            // 预约
            nodes = dom.DocumentElement.SelectNodes("reservations/request");
            patron.ReservationCount = nodes.Count;
            patron.ReservationCountHtml = ConvertToString(patron.ReservationCount);
            int arrivedCount = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                XmlNode node = nodes[i];
                string state = DomUtil.GetAttr(node, "state");
                if (state == "arrived")
                {
                    arrivedCount++;
                }
            }
            patron.ArrivedCount = arrivedCount;

            string strReservationWarningText = "";
            List<ReservationInfo> reservations = dp2WeiXinService.Instance.GetReservations(strPatronXml,
                out strReservationWarningText);
            patron.reservations = reservations;

            return patron;
        }

        // 2020/10/19 把电话拆分为座机和手机号
        public static void SplitTel(string telOrigin, out string pureTel, out string purePhone)
        {
            pureTel = "";
            purePhone = "";
            string[] list = telOrigin.Split(new char[] { ',' });
            foreach (string tel in list)
            {
                string one = tel.Trim();

                // 取第一个11位的数字为手机号
                if (one.Length == 11 && purePhone == "")
                {
                    purePhone = one;
                    continue;
                }

                if (pureTel != "")
                    pureTel += ",";
                pureTel += one;
            }
        }

        private string ConvertToString(int num)
        {
            string text = "";
            if (num > 0 && num <= 5)
            {
                text = "<span class='leftNum'>" + "▪".PadRight(num, '▪') + "</span>";
            }
            else if (num == 0)
            {
                text = "";
            }
            else
            {
                text = num.ToString();
            }

            if (text != "")
                text = "(" + text + ")";
            return text;
        }

        private string RemoveWeiXinId(string email)
        {
            //<email>test@163.com,123,weixinid:o4xvUviTxj2HbRqbQb9W2nMl4fGg,weixinid:o4xvUvnLTg6NnflbYdcS-sxJCGFo,weixinid:testid</email>
            string[] emailList = email.Split(new char[] { ',' });
            string clearEmail = "";
            for (int i = 0; i < emailList.Length; i++)
            {
                string oneEmail = emailList[i].Trim();
                if (oneEmail.Length > 9 && oneEmail.Substring(0, 9) == WeiXinConst.C_WeiXinIdPrefix)
                {
                    continue;
                }

                if (clearEmail != "")
                    clearEmail += ",";

                clearEmail += oneEmail;
            }

            return clearEmail;
        }

        public List<BorrowInfo2> GetBorrowInfo(string strXml, out string strWarningText,
            out string maxBorrowCountString,
            out string curBorrowCountString)
        {
            strWarningText = "";
            maxBorrowCountString = "";
            curBorrowCountString = "";
            List<BorrowInfo2> borrowList = new List<BorrowInfo2>();

            XmlDocument dom = new XmlDocument();
            dom.LoadXml(strXml);
            // ***
            // 在借册
            XmlNodeList nodes = dom.DocumentElement.SelectNodes("borrows/borrow");
            int nBorrowCount = nodes.Count;
            /*
<info>
<item name="可借总册数" value="10" />
<item name="日历名">
  <value>ILL1/新建日历</value>
</item>
<item name="当前还可借" value="10" />
</info>                  
             */
            XmlNode nodeMax = dom.DocumentElement.SelectSingleNode("info/item[@name='可借总册数']");
            if (nodeMax == null)
            {
                maxBorrowCountString = "获取当前读者可借总册数出错：未找到对应节点。";
            }
            else
            {
                string maxCount = DomUtil.GetAttr(nodeMax, "value");
                if (maxCount == "")
                {
                    maxBorrowCountString = "获取当前读者可借总册数出错：未设置对应值。";
                }
                else
                {
                    maxBorrowCountString = "最多可借:" + maxCount; ;
                    XmlNode nodeCurrent = dom.DocumentElement.SelectSingleNode("info/item[@name='当前还可借']");
                    if (nodeCurrent == null)
                    {
                        curBorrowCountString = "获取当前还可借出错：未找到对应节点。";
                    }
                    else
                    {
                        curBorrowCountString = "当前可借:" + DomUtil.GetAttr(nodeCurrent, "value");
                    }
                }
            }
            /*
              <borrows>
                <borrow barcode="C20" recPath="中文图书实体/10" biblioRecPath="中文图书/3" borrowDate="Mon, 28 Dec 2015 10:14:18 +0800" borrowPeriod="5day" returningDate="Sat, 02 Jan 2016 12:00:00 +0800" operator="ILL1-R002" type="童话" price="" isOverdue="yes" overdueInfo="已超过借阅期限 (2016年1月2日) 3 天。" overdueInfo1=" (已超期 3 天)" timeReturning="Sat, 02 Jan 2016 12:00:00 +0800" />
                <borrow barcode="C10" recPath="中文图书实体/3" biblioRecPath="中文图书/1" borrowDate="Mon, 28 Dec 2015 09:49:47 +0800" borrowPeriod="5day" returningDate="Sat, 02 Jan 2016 12:00:00 +0800" operator="ILL1-R002" type="童话" price="15" isOverdue="yes" overdueInfo="已超过借阅期限 (2016年1月2日) 3 天。" overdueInfo1=" (已超期 3 天)" timeReturning="Sat, 02 Jan 2016 12:00:00 +0800" />
                <borrow barcode="C32" recPath="中文图书实体/13" biblioRecPath="中文图书/2" borrowDate="Mon, 28 Dec 2015 04:15:39 +0800" borrowPeriod="5day" returningDate="Sat, 02 Jan 2016 12:00:00 +0800" operator="supervisor" type="童话" price="" isOverdue="yes" overdueInfo="已超过借阅期限 (2016年1月2日) 3 天。" overdueInfo1=" (已超期 3 天)" timeReturning="Sat, 02 Jan 2016 12:00:00 +0800" />
                <borrow barcode="C33" recPath="中文图书实体/14" biblioRecPath="中文图书/2" borrowDate="Mon, 28 Dec 2015 04:14:43 +0800" borrowPeriod="5day" returningDate="Sat, 02 Jan 2016 12:00:00 +0800" operator="supervisor" type="音乐" price="" isOverdue="yes" overdueInfo="已超过借阅期限 (2016年1月2日) 3 天。" overdueInfo1=" (已超期 3 天)" timeReturning="Sat, 02 Jan 2016 12:00:00 +0800" />
                <borrow barcode="C31" recPath="中文图书实体/12" biblioRecPath="中文图书/2" borrowDate="Mon, 28 Dec 2015 04:14:39 +0800" borrowPeriod="5day" returningDate="Sat, 02 Jan 2016 12:00:00 +0800" operator="supervisor" type="童话" price="" isOverdue="yes" overdueInfo="已超过借阅期限 (2016年1月2日) 3 天。" overdueInfo1=" (已超期 3 天)" timeReturning="Sat, 02 Jan 2016 12:00:00 +0800" />
                <borrow barcode="C23" recPath="中文图书实体/9" biblioRecPath="中文图书/3" borrowDate="Mon, 28 Dec 2015 04:14:36 +0800" borrowPeriod="5day" returningDate="Sat, 02 Jan 2016 12:00:00 +0800" operator="supervisor" type="童话" price="" isOverdue="yes" overdueInfo="已超过借阅期限 (2016年1月2日) 3 天。" overdueInfo1=" (已超期 3 天)" timeReturning="Sat, 02 Jan 2016 12:00:00 +0800" />
              </borrows>
            */
            int nOverdueCount = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                XmlNode node = nodes[i];

                //借阅基本信息
                string strBarcode = DomUtil.GetAttr(node, "barcode");
                string strBorrowDate = DateTimeUtil.LocalDate(DomUtil.GetAttr(node, "borrowDate"));
                string strPeriod = DomUtil.GetAttr(node, "borrowPeriod");
                string strOperator = DomUtil.GetAttr(node, "operator");
                string strTimeReturning = DateTimeUtil.LocalDate(DomUtil.GetAttr(node, "timeReturning"));

                // 续借信息
                string strRenewNo = DomUtil.GetAttr(node, "no"); // 续借次数
                string strRenewComment = DomUtil.GetAttr(node, "renewComment");

                string rowCss = "";
                string strOverdueInfo = DomUtil.GetAttr(node, "overdueInfo");
                string strOverdue1 = DomUtil.GetAttr(node, "overdueInfo1");
                bool bOverdue = false;
                string strIsOverdue = DomUtil.GetAttr(node, "isOverdue");
                if (strIsOverdue == "yes")
                {
                    bOverdue = true;
                    nOverdueCount++;

                    strTimeReturning += strOverdue1;

                    rowCss = "borrowinfo-overdue";
                }

                // 创建 borrowinfo对象，加到集合里
                BorrowInfo2 borrowInfo = new BorrowInfo2();
                borrowInfo.barcode = strBarcode;
                borrowInfo.renewNo = strRenewNo;
                borrowInfo.borrowDate = strBorrowDate;
                borrowInfo.period = strPeriod;
                borrowInfo.borrowOperator = strOperator;
                borrowInfo.renewComment = strRenewComment;
                borrowInfo.overdue = strOverdueInfo;
                borrowInfo.returnDate = strTimeReturning;
                //if (string.IsNullOrEmpty(this.opacUrl) == false)
                //    borrowInfo.barcodeUrl = this.opacUrl + "/book.aspx?barcode=" + strBarcode;
                //else
                //    borrowInfo.barcodeUrl = "";
                borrowInfo.barcodeUrl = "";
                borrowInfo.rowCss = rowCss;
                borrowList.Add(borrowInfo);

            }
            if (nOverdueCount > 0)
                strWarningText += "<div class='warning overdue'><div class='number'>" + nOverdueCount.ToString() + "</div><div class='text'>已超期</div></div>";

            return borrowList;
        }


        public List<OverdueInfo> GetOverdueInfo(string strXml, out string strWarningText)
        {
            strWarningText = "";
            List<OverdueInfo> overdueList = new List<OverdueInfo>();

            XmlDocument dom = new XmlDocument();
            dom.LoadXml(strXml);

            // ***
            // 违约/交费信息
            List<OverdueInfo> overdueLit = new List<OverdueInfo>();
            XmlNodeList nodes = dom.DocumentElement.SelectNodes("overdues/overdue");
            if (nodes.Count > 0)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    XmlNode node = nodes[i];
                    string strBarcode = DomUtil.GetAttr(node, "barcode");
                    string strOver = DomUtil.GetAttr(node, "reason");
                    string strBorrowPeriod = DomUtil.GetAttr(node, "borrowPeriod");
                    strBorrowPeriod = dp2WeiXinService.GetDisplayTimePeriodStringEx(strBorrowPeriod);
                    string strBorrowDate = LocalDateOrTime(DomUtil.GetAttr(node, "borrowDate"), strBorrowPeriod);
                    string strReturnDate = LocalDateOrTime(DomUtil.GetAttr(node, "returnDate"), strBorrowPeriod);
                    string strID = DomUtil.GetAttr(node, "id");
                    string strPrice = DomUtil.GetAttr(node, "price");
                    string strOverduePeriod = DomUtil.GetAttr(node, "overduePeriod");
                    string strComment = DomUtil.GetAttr(node, "comment");
                    if (String.IsNullOrEmpty(strComment) == true)
                        strComment = "&nbsp;";
                    string strPauseInfo = "";
                    // 把一行文字变为两行显示
                    //strBorrowDate = strBorrowDate.Replace(" ", "<br/>");
                    //strReturnDate = strReturnDate.Replace(" ", "<br/>");

                    OverdueInfo overdueInfo = new OverdueInfo();
                    overdueInfo.barcode = strBarcode;
                    //if (string.IsNullOrEmpty(this.opacUrl) == false)
                    //    overdueInfo.barcodeUrl = this.opacUrl + "/book.aspx?barcode=" + strBarcode;
                    //else
                    //    overdueInfo.barcodeUrl = "";
                    overdueInfo.barcodeUrl = "";
                    overdueInfo.reason = strOver;
                    overdueInfo.price = strPrice;
                    overdueInfo.pauseInfo = strPauseInfo;
                    overdueInfo.borrowDate = strBorrowDate;
                    overdueInfo.borrowPeriod = strBorrowPeriod;
                    overdueInfo.returnDate = strReturnDate;
                    overdueInfo.comment = strComment;
                    overdueLit.Add(overdueInfo);
                }

                strWarningText += "<div class='warning amerce'><div class='number'>" + nodes.Count.ToString() + "</div><div class='text'>待交费</div></div>";
            }

            return overdueLit;
        }

        public List<ReservationInfo> GetReservations(string strXml, out string strWarningText)
        {
            strWarningText = "";
            List<ReservationInfo> reservationList = new List<ReservationInfo>();

            XmlDocument dom = new XmlDocument();
            dom.LoadXml(strXml);

            // ***
            // 预约请求
            XmlNodeList nodes = dom.DocumentElement.SelectNodes("reservations/request");
            if (nodes.Count > 0)
            {
                int nArriveCount = 0;
                for (int i = 0; i < nodes.Count; i++)
                {
                    XmlNode node = nodes[i];

                    string strBarcodes = DomUtil.GetAttr(node, "items");
                    string strRequestDate = DateTimeUtil.LocalTime(DomUtil.GetAttr(node, "requestDate"));

                    string strOperator = DomUtil.GetAttr(node, "operator");
                    string strArrivedItemBarcode = DomUtil.GetAttr(node, "arrivedItemBarcode");

                    int nBarcodesCount = GetBarcodesCount(strBarcodes);
                    // 状态
                    string strArrivedDate = DateTimeUtil.LocalTime(DomUtil.GetAttr(node, "arrivedDate"));
                    string strState = DomUtil.GetAttr(node, "state");
                    string strStateText = "";
                    if (strState == "arrived")
                    {
                        strStateText = "册 " + strArrivedItemBarcode + " 已于 " + strArrivedDate + " 到书";

                        if (nBarcodesCount > 1)
                        {
                            strStateText += string.Format("; 同一预约请求中的其余 {0} 册旋即失效",  // "；同一预约请求中的其余 {0} 册旋即失效"
                                (nBarcodesCount - 1).ToString());
                        }

                        nArriveCount++;
                    }

                    string strBarcodesHtml = MakeBarcodeListHyperLink(strBarcodes, strArrivedItemBarcode, ", ")
                     + (nBarcodesCount > 1 ? " 之一" : "");

                    ReservationInfo reservationInfo = new ReservationInfo();
                    reservationInfo.pureBarcodes = strBarcodes;
                    reservationInfo.barcodes = strBarcodesHtml;
                    reservationInfo.state = strState;
                    reservationInfo.stateText = strStateText;
                    reservationInfo.requestdate = strRequestDate;
                    reservationInfo.operatorAccount = strOperator;
                    reservationInfo.arrivedBarcode = strArrivedItemBarcode;
                    reservationInfo.fullBarcodes = strBarcodes + "*" + strArrivedItemBarcode;
                    reservationList.Add(reservationInfo);
                }


                if (nArriveCount > 0)
                    strWarningText = "<div class='warning arrive'><div class='number'>" + nArriveCount.ToString() + "</div><div class='text'>预约到书</div></div>";
            }

            return reservationList;
        }
        int GetBarcodesCount(string strBarcodes)
        {
            string[] barcodes = strBarcodes.Split(new char[] { ',' });

            return barcodes.Length;
        }

        string MakeBarcodeListHyperLink(string strBarcodes,
            string strArrivedItemBarcode,
            string strSep)
        {
            string strResult = "";
            string strDisableClass = "";
            if (string.IsNullOrEmpty(strArrivedItemBarcode) == false)
                strDisableClass = "deleted";
            string[] barcodes = strBarcodes.Split(new char[] { ',' });
            for (int i = 0; i < barcodes.Length; i++)
            {
                string strBarcode = barcodes[i];
                if (String.IsNullOrEmpty(strBarcode) == true)
                    continue;

                if (strResult != "")
                    strResult += strSep;

                //strResult += "<a "
                //    + (string.IsNullOrEmpty(strDisableClass) == false && strBarcode != strArrivedItemBarcode ? "class='" + strDisableClass + "'" : "")
                //    + " href='"+this.opacUrl+ "/book.aspx?barcode=" + strBarcode+"'  target='_blank' " + ">" + strBarcode + "</a>";  

                strResult += "<a "
    + (string.IsNullOrEmpty(strDisableClass) == false && strBarcode != strArrivedItemBarcode ? "class='" + strDisableClass + "'" : "")
    + " href='#'" + ">" + strBarcode + "</a>";

            }

            return strResult;
        }

        // return:
        //      -1  检测过程发生了错误。应当作不能借阅来处理
        //      0   可以借阅
        //      1   证已经过了失效期，不能借阅
        //      2   证有不让借阅的状态
        public static int CheckReaderExpireAndState(XmlDocument readerdom,
            out string strError)
        {
            strError = "";

            string strExpireDate = DomUtil.GetElementText(readerdom.DocumentElement, "expireDate");
            if (String.IsNullOrEmpty(strExpireDate) == false)
            {
                DateTime expireDate;
                try
                {
                    expireDate = DateTimeUtil.FromRfc1123DateTimeString(strExpireDate);
                }
                catch
                {
                    strError = string.Format("借阅证失效期值s格式错误", // "借阅证失效期<expireDate>值 '{0}' 格式错误"
                        strExpireDate);

                    // "借阅证失效期<expireDate>值 '" + strExpireDate + "' 格式错误";
                    return -1;
                }

                DateTime now = DateTime.Now;//todo//this.Clock.UtcNow;
                if (expireDate <= now)
                {
                    // text-level: 用户提示
                    strError = string.Format("今天s已经超过借阅证失效期s",  // "今天({0})已经超过借阅证失效期({1})。"
                        now.ToLocalTime().ToLongDateString(),
                        expireDate.ToLocalTime().ToLongDateString());
                    return 1;
                }

            }

            string strState = DomUtil.GetElementText(readerdom.DocumentElement, "state");
            if (String.IsNullOrEmpty(strState) == false)
            {
                strError = string.Format("借阅证的状态为s", // "借阅证的状态为 '{0}'。"
                    strState);
                return 2;
            }

            return 0;
        }
        /// <summary>
        /// 得到的读者的联系方式
        /// </summary>
        /// <param name="dom"></param>
        /// <returns></returns>
        private static string GetContactString(XmlDocument dom)
        {
            string strTel = DomUtil.GetElementText(dom.DocumentElement, "tel");
            string strEmail = DomUtil.GetElementText(dom.DocumentElement, "email");
            string strAddress = DomUtil.GetElementText(dom.DocumentElement, "address");
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(strTel) == false)
                list.Add(strTel);
            if (string.IsNullOrEmpty(strEmail) == false)
            {
                strEmail = "";// JoinEmail(strEmail, "");
                list.Add(strEmail);
            }
            if (string.IsNullOrEmpty(strAddress) == false)
                list.Add(strAddress);
            return StringUtil.MakePathList(list, "; ");
        }

        /// <summary>
        /// 获取读者信息
        /// </summary>
        /// <param name="libId"></param>
        /// <param name="patronBarocde"></param>
        /// <param name="format"></param>
        /// <param name="xml"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int GetPatronXml(string libId,
            LoginInfo loginInfo,
            string patronBarocde,  //todo refID  @path:
            string format,
            out string recPath,
            out string timestamp,
            out string xml,
            out string strError)
        {
            xml = "";
            strError = "";
            recPath = "";
            timestamp = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                goto ERROR1;
            }

            // 使用传进来的账户，有可以是工作人员，也有可以是读者自己
            //LoginInfo loginInfo = new LoginInfo(userName, isPatron);

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SearchRequest request = new SearchRequest(id,
                loginInfo,
                "getPatronInfo",
                "",
                patronBarocde,
                "",
                "",
                "",
                format,
                1,
                0,
                -1);

            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

            REDO1:

                SearchResult result = connection.SearchTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 0, 15),  //改为15秒
                    cancel_token).Result;

                if (result.ResultCount == -1)
                {
                    bool bOffline = false;
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;


                    // 通道断了，重来一次
                    if (result.ErrorCode == "ChannelReleased")
                    {
                        goto REDO1;
                    }


                    //strError = this.GetFriendlyErrorInfo(result, lib.libName, out bOffline);//result.ErrorInfo;
                    return -1;
                }

                if (result.ResultCount == 0)
                {
                    strError = result.ErrorInfo;
                    return 0;
                }
                string path = result.Records[0].RecPath;
                int nIndex = path.IndexOf("@");
                path = path.Substring(0, nIndex);
                recPath = path;

                if (format.IndexOf("timestamp") != -1 && result.Records.Count > 1)
                {
                    timestamp = result.Records[1].Data;
                }

                xml = result.Records[0].Data;
                return 1;

            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }

        ERROR1:
            //WriteErrorLog("4返回-1，返回errorinfo:" + strError);

            return -1;
        }

        public string GetPurePath(string strBiblioPath)
        {
            int tempIndex = strBiblioPath.IndexOf("@");
            if (tempIndex > 0)
                return strBiblioPath.Substring(0, tempIndex);

            return strBiblioPath;
        }



        /// 根据code获取微信id
        public int GetWeiXinId(string code,
            GzhCfg gzh,
            out string weixinId,
            out string strError)
        {
            strError = "";
            weixinId = "";

            if (gzh == null)
            {
                strError = "gzh参数不能为null";
                return -1;
            }

            try
            {
                //用code换取access_token
                var result = OAuthApi.GetAccessToken(gzh.appId, gzh.secret, code);//this.weiXinAppId, this.weiXinSecret, code);
                if (result == null)
                {
                    strError = "GetAccessToken()返回的result为null。";
                    return -1;
                }
                if (result.errcode != ReturnCode.请求成功)
                {
                    strError = "获取微信id出错：" + result.errmsg;
                    return -1;
                }

                // 取出微信id
                weixinId = result.openid + "@" + gzh.appId; //2016-11-16，系统中使用的微信id都带上@appId
                return 0;
            }
            catch (Exception ex)
            {
                strError = "获取微信id异常：" + ex.Message;
                return -1;
            }
        }

        public int GetGzhAndLibs(string state,
            out GzhCfg gzh,
            out List<string> libList,
            out string strError)
        {
            strError = "";
            gzh = null;
            libList = new List<string>();

            if (String.IsNullOrEmpty(state) == true)
            {
                strError = "state参数为空";
                return -1;
            }

            // 2016-11-22
            //state格式：公众号名称:图书馆capo账户1,图书馆capo账户2
            int nIndex = state.IndexOf(":");
            string gzhName = state;
            string libCapoNames = "";
            string[] libs = null;
            if (nIndex != -1)
            {
                gzhName = state.Substring(0, nIndex);
                libCapoNames = state.Substring(nIndex + 1);
                if (libCapoNames != "")
                {
                    libs = libCapoNames.Split(new char[] { ',' });
                }
            }
            else if (state != "" && state != "ilovelibrary" && state != null)  // 这里有些问题，对接的第三方公众号怎么办
            {
                gzhName = "ilovelibrary";
                libCapoNames = state;
                if (libCapoNames != "")
                {
                    libs = libCapoNames.Split(new char[] { ',' });
                }
            }



            // 根据传进来的参数，得到公众号配置信息
            gzh = this._gzhContainer.GetByAppName(gzhName); //函数内会处理空的情况
            if (gzh == null)
            {
                strError = "验证失败：非正规途径 state=[" + state + "] gzhName=["+gzhName+"]进入！";
                return -1;
            }



            //得到可以访问的图书馆列表
            if (libs == null || libs.Length == 0)
            {
                //WriteLog1("debug:url参数中未指定图书馆，所以显示全部图书馆。");
                //未配时，全部图书馆
                foreach (Library lib in this.LibManager.Librarys)
                {
                    libList.Add(lib.Entity.id);
                }
            }
            else
            {
                //WriteLog1("debug:url参数中指定了图书馆["+ libCapoNames + "]");

                foreach (Library lib in this.LibManager.Librarys)
                {
                    if (libs.Contains(lib.Entity.capoUserName) == true)
                    // && string.IsNullOrEmpty(lib.Entity.state) == true) // 没有到期等  //todo 好像不应这里检查，如果这里不加入集合，后面会报找不到。
                    {
                        // WriteErrorLog1("***" + lib.Entity.capoUserName +"--"+lib.Entity.state);

                        libList.Add(lib.Entity.id);
                    }
                }
            }

            // 未设置要访问的图书馆
            if (libList.Count == 0)
            {
                strError = "验证失败：未设置要访问的图书馆";
                return -1;
            }
            return 0;
        }

        ///// <summary>
        ///// 发送客服消息
        ///// </summary>
        ///// <param name="openId"></param>
        ///// <param name="text"></param>
        //public void SendCustomerMsg(string openId, string text)
        //{
        //    var accessToken = AccessTokenContainer.GetAccessToken(this.weiXinAppId);
        //    CustomApi.SendText(accessToken, openId, "error");
        //}



        // 分析期限参数
        public static int ParsePeriodUnit(string strPeriod,
            out long lValue,
            out string strUnit,
            out string strError)
        {
            lValue = 0;
            strUnit = "";
            strError = "";

            strPeriod = strPeriod.Trim();

            if (String.IsNullOrEmpty(strPeriod) == true)
            {
                strError = "期限字符串为空";
                return -1;
            }

            string strValue = "";


            for (int i = 0; i < strPeriod.Length; i++)
            {
                if (strPeriod[i] >= '0' && strPeriod[i] <= '9')
                {
                    strValue += strPeriod[i];
                }
                else
                {
                    strUnit = strPeriod.Substring(i).Trim();
                    break;
                }
            }

            // 将strValue转换为数字
            try
            {
                lValue = Convert.ToInt64(strValue);
            }
            catch (Exception)
            {
                strError = "期限参数数字部分'" + strValue + "'格式不合法";
                return -1;
            }

            if (String.IsNullOrEmpty(strUnit) == true)
                strUnit = "day";   // 缺省单位为"天"

            strUnit = strUnit.ToLower();    // 统一转换为小写

            return 0;
        }

        #endregion



        #region 续借

        public int Circulation(string libId,
            LoginInfo loginInfo,
            string operation,
            string patron,
            string item,
            out string patronBarcode,
            out string patronXml,
            out ReturnInfo resultInfo,
            out string strError)
        {
            strError = "";
            resultInfo = null;
            patronBarcode = "";
            patronXml = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            // 使用的登录账号 20161025 jane
            //LoginInfo loginInfo = new LoginInfo(userName, isPatron);

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            CirculationRequest request = new CirculationRequest(id,
                loginInfo,
                operation,
                patron,
                item,
                "reader",  //style
                "xml",
                "",
                "");
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                CirculationResult result = connection.CirculationTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 10), // 10 秒
                    cancel_token).Result;

                resultInfo = result.ReturnInfo;
                patronBarcode = result.PatronBarcode;
                if (result.PatronResults != null && result.PatronResults.Count > 0)
                    patronXml = result.PatronResults[0];
                strError = result.ErrorInfo;

                if (result.Value == -1)
                {
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;
                    //strError = this.GetFriendlyErrorInfo(result, lib.libName);
                    return -1;
                }

                //Thread.Sleep(1000 * 5);

                return (int)result.Value;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
        }

        public int Renew1(string libId,
            string patron,
            string item,
            out string strError)
        {
            strError = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            // 使用读者账号 20161024 jane
            LoginInfo loginInfo = new LoginInfo(patron, true);

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            CirculationRequest request = new CirculationRequest(id,
                loginInfo,
                "renew",
                patron,
                item,
                "",
                "",
                "",
                "");
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                CirculationResult result = connection.CirculationTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 10), // 10 秒
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;
                    //strError = this.GetFriendlyErrorInfo(result, lib.libName);
                    return -1;
                }

                strError = result.ErrorInfo;
                return (int)result.Value;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
        }


        #endregion

        #region 预约


        public int Reservation(string weixinId,
            string libId,
            string patron,
            string items,
            string style,
            out string reserRowHtml,
            out string strError)
        {
            strError = "";
            reserRowHtml = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                return -1;
            }

            // 使用读者账号 20161024 jane
            LoginInfo loginInfo = new LoginInfo(patron, true);

            // 如果是取消获取一下读者记录
            string reserDate = "";
            string summary = "";
            if (style == "delete")
            {
                List<ReservationInfo> reserList = null;
                int nRet = this.GetPatronReservation(libId,
                    loginInfo,
                    patron,
                    out reserList,
                    out strError);
                if (nRet == -1)
                    return -1;
                if (reserList != null)
                {
                    foreach (ReservationInfo item in reserList)
                    {
                        if (items == item.pureBarcodes)
                        {
                            reserDate = item.requestdate;
                            break;
                        }
                    }
                }

                string strRecPath = "";
                nRet = GetBiblioSummary(loginInfo, lib, items, "", out summary, out strRecPath, out strError);
                if (nRet == -1)
                    return -1;
                summary = this.GetShortSummary(summary);

            }

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            CirculationRequest request = new CirculationRequest(id,
                loginInfo,
                "reservation",
                patron,
                items,
                style,
                "",
                "",
                "");
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                CirculationResult result = connection.CirculationTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 10), // 10 秒
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;
                    //strError = this.GetFriendlyErrorInfo(result, lib.libName);
                    return -1;
                }
                else
                {
                    strError = result.ErrorInfo;
                }



                if (style == "delete")
                {
                    reserRowHtml = this.getReservationHtml("未预约", items, true, true);
                }
                else if (style == "new")
                {

                    if (strError != "")
                        reserRowHtml = this.getReservationHtml("已到书", items, true, true);
                    else
                        reserRowHtml = this.getReservationHtml("已预约", items, true, true);

                    // 图书馆设置为支持在线预约，预约成功后，同时提示简化。2020/2/14 编译局使用的馆员备书功能
                    //if (string.IsNullOrEmpty(lib.comment) == false
                    //    && lib.comment.IndexOf("ReserveOnshelf") != -1)

                    // 2021/3/22改为如果设置了不给读者发送通知，则简化提示，以前是根据是否只有在架预约的
                    //if (lib.ReserveScope == LibDatabase.C_ReserveScope_OnlyOnshelf)  // 2020/3/22 改为使用参数
                    //2020/3/22 改为直接使用变量
                    // 2023/11/2 改为支持分馆，这个参数是一个复杂格式的字符串，例如：分馆1:N;分馆2:N
                    if (lib.IsSendArrivedNotice.Contains("N")==true) 
                    {
                        // 全局配置
                        if (lib.IsSendArrivedNotice == "N")
                        {
                            strError = ""; //将在架预约的提示清掉。
                        }
                        else //分馆配置
                        {
                            WxUserItem user = WxUserDatabase.Current.GetActive(weixinId);
                            if (user != null && user.type == WxUserDatabase.C_Type_Patron)
                            {
                                List<string> noSendArricedFenguanList = SplitArrivedParam(lib.IsSendArrivedNotice);
                                // 读者不属于不发消息的分馆，才会给这个读者发通知
                                if (noSendArricedFenguanList.Contains(user.libraryCode) == true)
                                {
                                    strError = ""; //将在架预约的提示清掉。
                                }
                            }
                        }


                    }

                }


                // 取消预约，发送微信通知
                if (style == "delete")
                {
                    try
                    {
                        WxUserItem user = WxUserDatabase.Current.GetActive(weixinId);
                        if (user == null)
                        {
                            strError = "取消预约时，不可能找不到当前绑定的读者账户";
                            return -1;
                        }
                        if (user.type == WxUserDatabase.C_Type_Worker)
                        {
                            strError = "异常，取消预约时，当前帐户应该是读者帐户";
                            return -1;
                        }

                        // 2021/8/3 增加屏蔽读者信息
                        string maskDef = this.GetMaskDef(user.libId, user.libraryCode, out bool send2PatronIsMask);

                        string operTime = DateTimeUtil.DateTimeToString(DateTime.Now);
                        //string fullPatronName = this.GetFullPatronName(user.readerName, user.readerBarcode, lib.libName, user.libraryCode, false);
                        //string strText = user.readerName + "，您已对上述图书取消预约,该书将不再为您保留。";
                        //string remark = "\n" + this._msgRemark;

                        string remark = user.readerName + "，您已对上述图书取消预约,该书将不再为您保留。";

                        //List<string> bindWeixinIds = new List<string>();
                        //string fullWeixinId = weixinId;//2016-11-16 传进来的weixinId带了@appId // + "@" + user.appId;
                        //bindWeixinIds.Add(fullWeixinId);

                        List<WxUserItem> bindPatronList = new List<WxUserItem>();
                        bindPatronList.Add(user);

                        // 得到找开tracing功能的工作人员微信id
                        List<WxUserItem> workers = this.GetTraceUsers(lib.id, user.libraryCode);

                        string first = "☀☀☀☀☀☀☀☀☀☀";
                        string first_color = "#9400D3";

                        //取消图书预约成功。
                        //书刊摘要：中国机读目录格式使用手册
                        //册条码号：B0000001
                        //预约日期：2017-10-01
                        //取消日期：2017-10-03
                        //证条码号：P000005
                        //张三，您取消图书预约成功，该书将不再为您保留。
                        string fullPatronBarcode = this.GetFullPatronName("", patron, lib.libName, user.libraryCode, false, maskDef);

                        // 2023/5/16 增加上读者姓名
                        fullPatronBarcode += " " + user.readerName;

                        CancelReserveTemplateData mData = new CancelReserveTemplateData(first,
                            first_color,
                            summary,
                            items,
                            reserDate,
                            operTime,
                            fullPatronBarcode,
                            remark);


                        //{{first.DATA}}
                        //标题：{{keyword1.DATA}}
                        //时间：{{keyword2.DATA}}
                        //内容：{{keyword3.DATA}}
                        //{{remark.DATA}}

                        //string title="取消预约成功";
                        //MessageTemplateData msgData = new MessageTemplateData(first,
                        //    first_color,
                        //    title,
                        //    operTime,
                        //    strText,
                        //    remark);

                        //证条码号处
                        string markFullPatronBarcode = this.GetFullPatronName("", patron, lib.libName, user.libraryCode, true, maskDef);
                        
                        //备注姓名
                        string markPatronName = dp2WeiXinService.Mask(maskDef, user.readerName, "name");//this.markString(user.readerName);
                        string tempRemark = remark.Replace(user.readerName, markPatronName);// +theOperator; ;

                        // 2023/5/16 增加上读者姓名
                        markFullPatronBarcode += " " + markPatronName;


                        CancelReserveTemplateData maskMsgData = new CancelReserveTemplateData(first,
                            first_color,
                            summary,
                            items,
                            reserDate,
                            operTime,
                            markFullPatronBarcode,
                            tempRemark);

                        //MessageTemplateData maskMsgData = new MessageTemplateData(first,
                        //    first_color,
                        //    title,
                        //    operTime,
                        //    strText,
                        //    remark);

                        int nRet = this.SendTemplateMsg1(GzhCfg.C_Template_CancelReserve,
                            bindPatronList,
                            workers,
                            mData,
                            maskMsgData,
                            this._messageLinkUrl,//"",
                            "",
                            send2PatronIsMask,
                            "",// originalXml  2023/6/7  我爱图书馆自己的消息，没有原始xml
                            out strError);
                        if (nRet == -1)
                            return -1;

                    }
                    catch (Exception ex)
                    {
                        this.WriteErrorLog("给读者" + patron + "发送'取消预约成功'通知异常：" + ex.Message);
                    }
                }

                //strError = result.ErrorInfo;
                return (int)result.Value;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }
        }


        #endregion


        #region 静态函数


        #region 读者信息脱敏
        public static string Mask(string maskDefinition,
    string text,
    string element_name)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            if (string.IsNullOrEmpty(maskDefinition))
                return text;
            // 找到 mask 方法
            var method = StringUtil.GetParameterByPrefix(maskDefinition, element_name);
            if (method == null) // 没有定义此元素
                return text;
            if (method == "" || method == "mask")   // 默认的 mask 方法: 全部替换为星号
                return new string('*', text.Length);

            if (method == "clear")
                return "";

            // 2|3 表示保留前 2 字符和末尾 3 字符，中间部分全部替换为星号
            if (method.Contains("|"))
                return Method1(method, text);
            return $"error: 无法识别的 method '{method}'";
        }

        static string Method1(string def, string text)
        {
            var parts = StringUtil.ParseTwoPart(def, "|");
            string left = parts[0];
            string right = parts[1];
            if (string.IsNullOrEmpty(left))
                left = "0";
            if (string.IsNullOrEmpty(right))
                right = "0";
            if (Int32.TryParse(left, out int left_chars) == false)
                return $"error: '{def}' 定义不合法。竖线左侧应该是一个数字";
            if (Int32.TryParse(right, out int right_chars) == false)
                return $"error: '{def}' 定义不合法。竖线右侧应该是一个数字";
            if (left_chars + right_chars >= text.Length
                || left_chars >= text.Length
                || right_chars >= text.Length)
                return text;
            string left_text = text.Substring(0, left_chars);
            string right_text = text.Substring(text.Length - right_chars, right_chars);
            int middle_chars = text.Length - left_chars - right_chars;
            return left_text + new string('*', middle_chars) + right_text;
        }

        #endregion

        public static LoginInfo GetLoginInfo(string loginUserName, string loginUserType)
        {
            bool isPatron = false;
            if (loginUserType == "patron")
                isPatron = true;
            LoginInfo loginInfo = new LoginInfo(loginUserName, isPatron);

            return loginInfo;
        }

        // 把整个字符串中的时间单位变换为可读的形态
        public static string GetDisplayTimePeriodStringEx(string strText)
        {
            if (string.IsNullOrEmpty(strText) == true)
                return "";
            strText = strText.Replace("day", "天");
            return strText.Replace("hour", "小时");
        }

        // 根据strPeriod中的时间单位(day/hour)，返回本地日期或者时间字符串
        // parameters:
        //      strPeriod   原始格式的时间长度字符串。也就是说，时间单位不和语言相关，是"day"或"hour"
        public static string LocalDateOrTime(string strTimeString,
            string strPeriod)
        {
            string strError = "";
            long lValue = 0;
            string strUnit = "";
            int nRet = ParsePeriodUnit(strPeriod,
                        out lValue,
                        out strUnit,
                        out strError);
            if (nRet == -1)
                strUnit = "day";
            if (strUnit == "day")
                return DateTimeUtil.LocalDate(strTimeString);

            return DateTimeUtil.LocalTime(strTimeString);
        }





        #endregion

        #region 错误日志

        public void WriteDebug2(string strText)
        {
            this.WriteLogInternal("DEBUG:" + strText, 2);
        }

        public void WriteDebug(string strText)
        {
            this.WriteLogInternal("DEBUG:" + strText, 1);
        }

        public void WriteErrorLog(string strText)
        {
            this.WriteLogInternal("ERROR:" + strText, 1);
        }


        private void WriteLogInternal(string strText, int logLevel)
        {
            // todo 有空比对下谢老师写日志的代码
            //DateTime now = DateTime.Now;
            //// 每天一个日志文件
            //string strFilename = Path.Combine(this.LogDir, "log_" + DateTimeUtil.DateTimeToString8(now) + ".txt");
            //string strTime = now.ToString();
            //FileUtil.WriteText(strFilename,
            //    strTime + " " + strText + "\r\n");

            if (logLevel <= this.LogLevel)
            {
                var logDir = this._weiXinLogDir;
                string strFilename = string.Format(logDir + "/log_{0}.txt", DateTime.Now.ToString("yyyyMMdd"));
                dp2WeiXinService.WriteLog(strFilename, strText, "dp2weixin");
            }
        }


        /// <summary>
        /// 写日志
        /// </summary>
        /// <param name="strText"></param>
        public static void WriteLog(string strFilename, string strText, string strEventLogSource)
        {
            try
            {
                //lock (logSyncRoot)
                {
                    string strTime = DateTime.Now.ToString();
                    StreamUtil.WriteText(strFilename, strTime + " " + strText + "\r\n");
                }
            }
            catch (Exception ex)
            {
                EventLog Log = new EventLog();
                Log.Source = strEventLogSource;
                Log.WriteEntry("因为原本要写入日志文件的操作发生异常， 所以不得不改为写入 Windows 日志(见后一条)。异常信息如下：'" + ExceptionUtil.GetDebugText(ex) + "'", EventLogEntryType.Error);
                Log.WriteEntry(strText, EventLogEntryType.Error);
            }
        }

        // 日期转换成yyyy-MM-dd HH:mm:ss格式字符串
        public static string DateTimeToStringNoSec(DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm");
        }

        public static string GetNowTime()
        {
            return DateTimeToStringNoSec(DateTime.Now);
        }

        /// <summary>
        /// 获得异常信息
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <returns></returns>
        public static string GetExceptionMessage(Exception ex)
        {
            string strResult = ex.GetType().ToString() + ":" + ex.Message;
            while (ex != null)
            {
                if (ex.InnerException != null)
                    strResult += "\r\n" + ex.InnerException.GetType().ToString() + ": " + ex.InnerException.Message;

                ex = ex.InnerException;
            }

            return strResult;
        }

        #endregion

        #region 二维码
        public int GetQRcode(string libId,
            string patronBarcode,
            out string code,
            out string strError)
        {
            strError = "";
            code = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                goto ERROR1;
            }

            //Operation:verifyPassword
            //Patron: !getpatrontempid:R0000001
            //Item:
            //ResultValue返回1表示成功，-1或0表示不成功。
            //成功的情况下，ErrorInfo成员里面返回了二维码字符串，形如” PQR:R0000001@00JDURE5FT1JEOOWGJV0R1JXMYI”
            string patron = "!getpatrontempid:" + patronBarcode;


            // 2020-2-28,因为读者可以自己注册，但读者注册完还没证条码，所以不能登录  //new LoginInfo("", false); //
            //LoginInfo loginInfo = new LoginInfo("", false); //new LoginInfo(patronBarcode, true);// 使用读者账号 20161024 jane
            // 2021/8/4 改为使用读者自己的身份
            LoginInfo loginInfo = new LoginInfo(patronBarcode, true);

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            CirculationRequest request = new CirculationRequest(id,
                loginInfo,
                "verifyPassword",
                patron,
                "",
                "", // style,
                "", // patronFormatList,
                "", // itemFormatList,
                ""); //biblioFormatList);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                CirculationResult result = connection.CirculationTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 10), // 10 秒
                    cancel_token).Result;
                if (result.Value == -1 || result.Value == 0)
                {
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;
                    //strError = this.GetFriendlyErrorInfo(result, lib.libName); //"出错：" + result.ErrorInfo;
                    return (int)result.Value;
                }

                // 成功
                code = result.ErrorInfo;
                return (int)result.Value;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }


        ERROR1:
            return -1;
        }

        // 把特征码转成二维码图片
        public void GetQrImage(
            string strCode,
            int nWidth,
            int nHeight,
            Stream outputStream,
            out string strError)
        {
            strError = "";
            try
            {
                if (nWidth <= 0)
                    nWidth = 300;
                if (nHeight <= 0)
                    nHeight = 300;

                MultiFormatWriter writer = new MultiFormatWriter(); //BarcodeWriter
                ByteMatrix bm = writer.encode(strCode, BarcodeFormat.QR_CODE, nWidth, nHeight);// 300, 300);
                using (Bitmap img = bm.ToBitmap())
                {
                    //MemoryStream ms = new MemoryStream();
                    img.Save(outputStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                    //return ms;
                }
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                //return null;
            }
        }

        /// <summary>
        /// 根据文字生成图片
        /// </summary>
        /// <param name="strError"></param>
        /// <returns></returns>
        public MemoryStream GetErrorImg(string strError)
        {
            Bitmap bmp = new Bitmap(210, 110);
            Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.White);


            Font font = SystemFonts.DefaultFont;
            Brush fontBrush = Brushes.Red;// SystemBrushes.ControlText;
            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            Rectangle rect = new Rectangle(2, 2, 200, 100);
            g.DrawString(strError, font, fontBrush, rect, sf);

            //g.FillRectangle(Brushes.Red, 2, 2, 100, 100);
            //g.DrawString(strError, new Font("微软雅黑", 10f), Brushes.Black, new PointF(5f, 5f));

            MemoryStream ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            g.Dispose();
            bmp.Dispose();

            return ms;
        }


        public int GetObjectMetadata(LoginInfo loginInfo,
            string libId,
            string weixinId,
            string objectPath,
            string style,
            Stream outputStream,
            out string metadata,
            out string timestamp,
            out string outputpath,
            out string strError)
        {
            strError = "";
            metadata = "";
            timestamp = "";
            outputpath = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                goto ERROR1;
            }

            // 2021/8/2 改为使用外面传进来的loginInfo
            // 使用代理账号capo 20161024 jane
            //LoginInfo loginInfo = Getdp2AccoutForSearch(weixinId);// new LoginInfo("", false);
            //if (loginInfo == null)//todo
            //{
            //    loginInfo = new LoginInfo("", false);
            //}

            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            GetResRequest request = new GetResRequest(id,
                loginInfo,
                "getRes",
                objectPath,
                0,
                -1,
                style);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;
                GetResResponse result = connection.GetResTaskAsync(
                   lib.capoUserName,
                    request,
                    outputStream,
                    null,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;

                // GetResResponse result = connection.GetResAsyncLite(
                // lib.capoUserName,
                // request,
                // outputStream,
                // null,
                // new TimeSpan(0, 1, 0),
                // cancel_token).Result;

                if (String.IsNullOrEmpty(result.ErrorCode) == false)
                {
                    //如果是得到 getRes 返回的错误码表示 AccessDenied，需要报错成“权限不够”之类，不要当作普通“出错”来报错，这样读者感觉会好一点。这个不着急改，记下来就行
                    if (result.ErrorCode == "AccessDenied")
                    {
                        strError = result.ErrorInfo;
                    }
                    else
                    {
                        strError = "调GetRes出错：" + result.ErrorInfo;
                    }
                    return -1;
                }

                if (result.TotalLength == -1)
                {
                    strError = "调GetRes出错：" + result.ErrorInfo;
                    return -1;
                }

                // 成功
                metadata = result.Metadata;
                timestamp = result.Timestamp;
                outputpath = result.Path;
                return 0;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }


        ERROR1:
            return -1;
        }


        public int GetPDFCount(string libId,
            LoginInfo loginInfo,
    string objectPath,
    out string filename,
    out string strError)
        {
            strError = "";
            filename = "";

            LibEntity lib = this.GetLibById(libId);
            if (lib == null)
            {
                strError = "未找到id为[" + libId + "]的图书馆定义。";
                goto ERROR1;
            }

            // 2023/6/12 使用传下来的帐户
            // 使用代理账号capo 20161024 jane
            //LoginInfo loginInfo = new LoginInfo("", false);


            CancellationToken cancel_token = new CancellationToken();


            string id = Guid.NewGuid().ToString();
            GetResRequest request = new GetResRequest(id,
                loginInfo,
                "getRes",
                objectPath + "/page:?",
                0,
                0,
                "data,metadata");
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                MemoryStream s = new MemoryStream();
                GetResResponse result = connection.GetResTaskAsync(
                   lib.capoUserName,
                    request,
                    s,
                    null,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;

                if (String.IsNullOrEmpty(result.ErrorCode) == false)
                {
                    strError = "调GetRes出错：" + result.ErrorInfo;
                    return -1;
                }

                if (result.TotalLength == -1)
                {
                    strError = "调GetRes出错：" + result.ErrorInfo;
                    return -1;
                }

                string metadata = result.Metadata;
                XmlDocument dom = new XmlDocument();
                try
                {
                    dom.LoadXml(metadata);
                }
                catch (Exception ex)
                {
                    strError = "加载pdf的metadata到dom出错：" + ex.Message;
                    goto ERROR1;
                }

                filename = DomUtil.GetAttr(dom.DocumentElement, "localpath");
                int nIndex = filename.LastIndexOf('\\');
                if (nIndex > 0)
                {
                    filename = filename.Substring(nIndex + 1);
                }


                // 成功
                return (int)result.TotalLength;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }


        ERROR1:
            return -1;
        }

        #endregion

        #region 消息：公告，好书，图书馆介绍

        public int GetMessage(string group,
            string libId,
             string msgId,
            string subjectCondition,
            string style,
            out List<MessageItem> list,
            out string strError)
        {
            list = new List<MessageItem>();
            strError = "";

            string libraryCode = "";
            if (libId.IndexOf('/') != -1)
            {
                int nIndex = libId.IndexOf('/');
                libraryCode = libId.Substring(nIndex + 1);
                libId = libId.Substring(0, nIndex);
            }



            List<MessageRecord> records = new List<MessageRecord>();
            int nRet = this.GetMessageInternal("", // action
                group,
                libId,
                msgId,
                subjectCondition,
                out records,
                out strError);
            if (nRet == -1)
                return -1;

            foreach (MessageRecord record in records)
            {
                MessageItem item = ConvertMsgRecord(group, record, style, libId, libraryCode);
                list.Add(item);
            }

            return nRet;
        }

        public MessageItem ConvertMsgRecord(string group,
            MessageRecord record,
            string style,
            string libId,
            string libraryCode)
        {

            MessageItem item = new MessageItem();
            item.id = record.id;
            item.publishTime = DateTimeUtil.DateTimeToString(record.publishTime);
            item.subject = "";
            if (record.subjects != null && record.subjects.Length > 0)
            {
                item.subject = record.subjects[0];

                // 2022.7.4注册掉，一下子不理解原来为啥要转义
                //item.subject = StringUtil.UnescapeString(item.subject);

            }
            string title = "";
            string content = "";
            string format = "text"; //默认是text样式
            string creator = "";
            string remark = "";

            string xml = record.data;
            XmlDocument dom = new XmlDocument();
            try
            {
                dom.LoadXml(xml);
                XmlNode root = dom.DocumentElement;
                XmlNode nodeTitle = root.SelectSingleNode("title");
                if (nodeTitle != null)
                    title = nodeTitle.InnerText;

                XmlNode nodeContent = root.SelectSingleNode("content");
                if (nodeTitle != null)
                    content = nodeContent.InnerText;

                XmlNode nodeRemark = root.SelectSingleNode("remark");
                if (nodeRemark != null)
                    remark = nodeRemark.InnerText;

                format = DomUtil.GetAttr(nodeContent, "format");
                if (format == "")
                    format = "text";

                XmlNode nodeCreator = root.SelectSingleNode("creator");
                if (nodeCreator != null)
                    creator = DomUtil.GetNodeText(nodeCreator);
            }
            catch
            {
                title = "不符合格式的消息";
                content = "不符合格式的消息-" + xml;
            }

            item.title = title;
            item.contentFormat = format;
            item.creator = creator;
            item.content = "";
            item.remark = remark;

            if (style == "original")
            {
                item.content = content;
            }
            else if (style == "browse")
            {
                item.content = "";

                string contentHtml = "";
                if (group == C_Group_Bb || group == C_Group_HomePage || group == C_Group_dp_home)
                {
                    contentHtml = GetMsgHtml(format, content, libId, libraryCode);
                }
                else if (group == C_Group_Book)
                {
                    contentHtml = GetBookHtml(format, content, libId);

                }
                item.contentHtml = contentHtml;

                if (String.IsNullOrEmpty(item.remark) == false)
                {
                    item.remarkHtml = GetMsgHtml("text", item.remark, libId, libraryCode);
                }
            }
            return item;
        }

        public string GetMsgHtml(string format, string content, string libId, string libraryCode)
        {
            string contentHtml = "";
            if (format == "markdown")
            {
                contentHtml = CommonMark.CommonMarkConverter.Convert(content);
            }
            else
            {
                //普通text 处理换行
                //contentHtml = HttpUtility.HtmlEncode(content);
                //contentHtml = contentHtml.Replace("\r\n", "\n");
                //contentHtml = contentHtml.Replace("\n", "<br/>");

                content = content.Replace("\r\n", "\n");
                content = content.Replace("\r", "\n");
                string[] list = content.Split(new char[] { '\n' });
                foreach (string str in list)
                {
                    if (contentHtml != "")
                        contentHtml += "<br/>";

                    string html = HttpUtility.HtmlEncode(str);



                    // 20170419,公告支持url超链接了,
                    // 当http:开头时，后面整个部分作为url，
                    //如果想将url放中间，后面跟其它文字，需要这样配：文字部分{http://www.***.com}文字部分
                    string left = "";
                    string middle = "";
                    string right = "";
                    int nIndex = html.IndexOf("{http:");
                    if (nIndex == -1)
                        nIndex = html.IndexOf("{https:");
                    if (nIndex >= 0)
                    {
                        left = html.Substring(0, nIndex);
                        right = html.Substring(nIndex + 1); //不带{
                        nIndex = right.IndexOf("}");
                        if (nIndex >= 0)
                        {
                            middle = right.Substring(0, nIndex); //不带}
                            right = right.Substring(nIndex + 1);
                        }
                        else
                        {
                            middle = right;
                            right = "";
                        }
                    }
                    else
                    {
                        nIndex = html.IndexOf("http:");
                        if (nIndex == -1)
                            nIndex = html.IndexOf("https:");
                        if (nIndex >= 0)
                        {
                            left = html.Substring(0, nIndex);
                            middle = html.Substring(nIndex);
                            right = "";
                        }
                    }
                    if (string.IsNullOrEmpty(middle) == false)
                    {
                        middle = "<a href='" + middle + "'>" + middle + "</a>";
                        html = left + middle + right;
                    }
                    //===========

                    contentHtml += html;


                }
            }

            // 替换宏
            if (contentHtml.Contains(LibraryManager.M_Lib_WxPatronCount) == true
                || contentHtml.Contains(LibraryManager.M_Lib_WxWorkerCount) == true
                || contentHtml.Contains(LibraryManager.M_Lib_WxTotalCount) == true

                || contentHtml.Contains(LibraryManager.M_Lib_WebPatronCount) == true
                || contentHtml.Contains(LibraryManager.M_Lib_WebWorkerCount) == true
                || contentHtml.Contains(LibraryManager.M_Lib_WebTotalCount) == true

                || contentHtml.Contains(LibraryManager.M_Lib_BindTotalCount) == true
                )
            {

                List<WxUserItem> wxPatronList = new List<WxUserItem>();

                // 2022/7/20 webPatronList也包括小程序来源
                List<WxUserItem> webPatronList = new List<WxUserItem>();

                List<WxUserItem> wxWorkerList = new List<WxUserItem>();

                // 2022/7/20 webWorkerList也包括小程序来源
                List<WxUserItem> webWorkerList = new List<WxUserItem>();

                this.GetBind(libId,
                    libraryCode,
                    out wxPatronList,
                    out webPatronList,
                    out wxWorkerList,
                    out webWorkerList);

                int wxTotalCount = wxPatronList.Count + wxWorkerList.Count;
                int webTotalCount = webPatronList.Count + webWorkerList.Count;
                int bindTotalCount = wxTotalCount + webTotalCount;


                contentHtml = contentHtml.Replace(LibraryManager.M_Lib_WxPatronCount, wxPatronList.Count.ToString());
                contentHtml = contentHtml.Replace(LibraryManager.M_Lib_WxWorkerCount, wxWorkerList.Count.ToString());
                contentHtml = contentHtml.Replace(LibraryManager.M_Lib_WxTotalCount, wxTotalCount.ToString());

                // 新增加的web绑定统计
                contentHtml = contentHtml.Replace(LibraryManager.M_Lib_WebPatronCount, webPatronList.Count.ToString());
                contentHtml = contentHtml.Replace(LibraryManager.M_Lib_WebWorkerCount, webWorkerList.Count.ToString());
                contentHtml = contentHtml.Replace(LibraryManager.M_Lib_WebTotalCount, webTotalCount.ToString());

                // 总人数
                contentHtml = contentHtml.Replace(LibraryManager.M_Lib_BindTotalCount, bindTotalCount.ToString());

            }



            return contentHtml;
        }


        public void GetBind(string libId,
            string libraryCode,
            out List<WxUserItem> wxPatronList,
            out List<WxUserItem> webPatronList,
            out List<WxUserItem> wxWorkerList,
            out List<WxUserItem> webWorkerList)
        {
            wxPatronList = new List<WxUserItem>();

            // 2022/7/20 webPatronList也包括小程序来源
            webPatronList = new List<WxUserItem>();

            wxWorkerList = new List<WxUserItem>();

            // 2022/7/20 webWorkerList也包括小程序来源
            webWorkerList = new List<WxUserItem>();

            // 获取绑定的读者数量
            List<WxUserItem> patrons = WxUserDatabase.Current.Get("", libId, libraryCode, WxUserDatabase.C_Type_Patron, null, null, true);
            foreach (WxUserItem user in patrons)
            {
                if (WxUserDatabase.CheckFrom(user.weixinId) == WxUserDatabase.C_from_web //CheckIsFromWeb(user.weixinId) == true)//user.weixinId.Length > 2 && user.weixinId.Substring(0, 2) == "~~")
                || WxUserDatabase.CheckFrom(user.weixinId) == WxUserDatabase.C_from_applet)
                {
                    webPatronList.Add(user);
                }
                else
                {
                    wxPatronList.Add(user);
                }
            }

            // 获取绑定的工作人员数量
            List<WxUserItem> workers = WxUserDatabase.Current.Get("", libId, libraryCode, WxUserDatabase.C_Type_Worker, null, null, true);
            foreach (WxUserItem user in workers)
            {
                if (user.userName == WxUserDatabase.C_Public)
                {
                    continue;
                }

                if (WxUserDatabase.CheckFrom(user.weixinId) == WxUserDatabase.C_from_web
                    || WxUserDatabase.CheckFrom(user.weixinId) == WxUserDatabase.C_from_applet) //.CheckIsFromWeb(user.weixinId) == true)//user.weixinId.Length > 2 && user.weixinId.Substring(0, 2) == "~~")
                {
                    webWorkerList.Add(user);
                }
                else
                {
                    wxWorkerList.Add(user);
                }
            }

        }

        public static bool CheckIsBiblioPath(string text)
        {
            bool bPath = false;
            int index = text.LastIndexOf('/');
            if (index > 0) //因为路径中的/不可能是第一个字符，所以没有-1
            {
                string right = text.Substring(index + 1);
                try
                {
                    int no = Convert.ToInt32(right);
                    if (no >= 0)
                        bPath = true;
                }
                catch
                { }
            }

            return bPath;
        }


        public string GetBookHtml(string format, string content, string libId)
        {
            string contentHtml = "";

            content = content.Replace("\r\n", "\n");
            content = content.Replace("\r", "\n");
            string[] list = content.Split(new char[] { '\n' });
            foreach (string str in list)
            {
                // 检查是不是书目路径
                bool bPath = dp2WeiXinService.CheckIsBiblioPath(str);
                if (bPath == false)
                {

                    if (format == "markdown")
                    {
                        contentHtml += CommonMark.CommonMarkConverter.Convert(str);
                    }
                    else
                    {
                        contentHtml += "<div style='color:gray'>" + HttpUtility.HtmlEncode(str) + "</div>";
                    }

                }
                else
                {
                    var word = "@bibliorecpath:" + str;
                    string detalUrl = "/Biblio/Detail?biblioPath=" + HttpUtility.UrlEncode(str);
                    contentHtml += "<div style='padding-top:4px;'><a href='javascript:void(0)' onclick='gotoBiblioDetail(\"" + detalUrl + "\")'>" + HttpUtility.HtmlEncode(str) + "</a></div>";
                    contentHtml += "<div  class='pending' style='padding-bottom:4px'>"
                                           + "<label>bs-" + word + "</label>"
                                           + "<img src='../img/wait2.gif' />"
                                           + "<span>" + libId + "</span>"
                                       + "</div>";
                }

            }

            return contentHtml;
        }

        // 从 dp2mserver 获得消息
        // 每次最多获得 100 条
        /// <summary>
        /// 获取消息
        /// </summary>
        /// <param name="action"></param>
        /// <param name="group"></param>
        /// <param name="libId"></param>
        /// <param name="msgId"></param>
        /// <param name="subjectCondition"></param>
        /// <param name="records"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        private int GetMessageInternal(string action,
            string group,
            string libId,
            string msgId,
            string subjectCondition,
            out List<MessageRecord> records,
            out string strError)
        {
            strError = "";
            records = new List<MessageRecord>();
            //return 0;

            // 20220704测试删除用户数据使用
            //subjectCondition = "2022年6月新书目(2)";

            // 20220701 将()[]|符号转义
            subjectCondition = StringUtil.EscapeString(subjectCondition, "[](),|");

            string wxUserName = "";
            string libName = "";

            // string connName
            string connName = C_ConnPrefix_Myself + libId;   //"<myself>:";
            if (group == C_Group_dp_home)
            {
                connName = C_ConnPrefix_Myself + C_ConnName_dp;   //"_dp_";

                wxUserName = this.GetUserName();
                libName = "";
            }
            else
            {
                if (string.IsNullOrEmpty(libId) == true)
                {
                    strError = "libId参数不能为空";
                    return -1;
                }

                // 取出用户名
                LibEntity lib = this.GetLibById(libId);
                if (lib == null)
                {
                    strError = "未找到id='" + libId + "'对应的图书馆";
                    return -1;
                }
                wxUserName = lib.wxUserName;
                libName = lib.libName;
            }

            // 这里要转换一下，接口传进来的是转义后的
            //subjectCondition = HttpUtility.HtmlDecode(subjectCondition);

            string sortCondition = "publishTime|desc";
            if (group == dp2WeiXinService.C_Group_HomePage)
                sortCondition = "publishTime|asc";


            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            GetMessageRequest request = new GetMessageRequest(id,
                action,
                group,
                wxUserName,
                "", // strTimeRange,
                sortCondition,//sortCondition 按发布时间倒序排
                msgId, //IdCondition 
                subjectCondition,
                0,
                100);  // todo 如果超过100条，要做成递归来调
            try
            {
                MessageConnection connection = this.Channels.GetConnectionTaskAsync(this._dp2MServerUrl,
                    connName).Result;
                GetMessageResult result = connection.GetMessage(request,
                    new TimeSpan(0, 0, 10),
                    cancel_token);
                if (result.Value == -1)
                {
                    strError = "图书馆[" + libName + "]返回错误:" + result.ErrorInfo;
                    //strError = this.GetFriendlyErrorInfo(result, libName);
                    goto ERROR1;
                }

                records = result.Results;
                return result.Results.Count;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }
        ERROR1:
            strError = "访问服务器异常: " + strError;
            return -1;
        }




        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="libUserName"></param>
        /// <param name="style"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        public int CoverMessage(WxUserItem worker,
            string weixinId,
            string group,
            string libId,
            MessageItem item,
            string style,
            string parameters,
            out MessageItem returnItem,
            out string strError)
        {
            strError = "";
            returnItem = null;

            string libraryCode = "";
            if (libId.IndexOf('/') != -1)
            {
                int nIndex = libId.IndexOf('/');
                libraryCode = libId.Substring(nIndex + 1);
                libId = libId.Substring(0, nIndex);
            }

            if (worker == null || worker.type != WxUserDatabase.C_Type_Worker)
            {
                strError = "当前帐户是工作人员，才能编辑消息";
                return -1;
            }




            strError = checkGroup(group);
            if (strError != "")
            {
                goto ERROR1;
            }

            if (String.IsNullOrEmpty(item.creator) == true)
            {
                strError = "未传入creator";
                goto ERROR1;
            }

            // 2016-8-24 超级管理员可以编辑任何图书馆介绍与公告,注意这个条件判断是取反
            if (!((group == dp2WeiXinService.C_Group_Bb
                || group == dp2WeiXinService.C_Group_HomePage
                || group == dp2WeiXinService.C_Group_dp_home)
                && item.creator == dp2WeiXinService.C_Supervisor))
            {
                // 检索工作人员是否有权限 _wx_setbb
                string needRight = dp2WeiXinService.GetNeedRight(group);

                LibEntity lib = this.GetLibById(libId);
                if (lib == null)
                {
                    strError = "根据id[" + libId + "]未找到对应的图书馆配置";
                    goto ERROR1;
                }


                int nHasRights = this.CheckRights(worker, lib, needRight, out strError);
                if (nHasRights == -1)
                {
                    strError = "用账户名'" + item.creator + "'获取工作人员账户出错：" + strError;
                    goto ERROR1;
                }
                if (nHasRights == 0)
                {
                    strError = "帐户[" + item.creator + "]没有" + needRight + "权限";
                    goto ERROR1;
                }
            }


            string connName = C_ConnPrefix_Myself + libId;
            if (group == dp2WeiXinService.C_Group_dp_home)
                connName = C_ConnPrefix_Myself + C_ConnName_dp;// "_dp_";

            string strText = "";
            if (style != "delete")
            {
                strText = "<body>"
                + "<title>" + HttpUtility.HtmlEncode(item.title) + "</title>"  //前端传过来时，已经转义过了 HttpUtility.HtmlEncode(item.title)
                + "<content format='" + item.contentFormat + "'>" + HttpUtility.HtmlEncode(item.content) + "</content>"
                + "<remark>" + HttpUtility.HtmlEncode(item.remark) + "</remark>"
                + "<creator>" + item.creator + "</creator>"
                + "</body>";
            }

            List<MessageRecord> records = new List<MessageRecord>();
            MessageRecord record = new MessageRecord();
            record.id = item.id;
            record.groups = group.Split(new char[] { ',' });
            record.creator = "";    // 服务器会自己填写
            record.data = strText;
            record.format = "text";
            record.type = "message";
            record.thread = "";
            record.expireTime = new DateTime(0);    // 表示永远不失效
            if (item.subject != null)
                item.subject = item.subject.Trim();// 2016-8-20 jane 对栏目首尾去掉空白
            if (item.subject != null && item.subject != "")
            {
                // 2022.7.4 注释，一下子不理解为啥要转义存储
                //string tempSubject = StringUtil.EscapeString(item.subject, "[](),|");
                string tempSubject = item.subject;
                record.subjects = new string[] { tempSubject };//2016-8-20,不管有没有逗号，只当作一条subject处理。item.subject.Split(new char[] { ',' },StringSplitOptions.RemoveEmptyEntries); // 2016-8-20，jane,对首尾去掉空白，与服务器保存一致。
            }
            else
                record.subjects = new string[] { };
            records.Add(record);

            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    connName).Result;
                SetMessageRequest param = new SetMessageRequest(style,
                    "",
                    records);
                CancellationToken cancel_token = new CancellationToken();
                SetMessageResult result = connection.SetMessageTaskAsync(param,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.Value == -1)
                {
                    strError = result.ErrorInfo;
                    goto ERROR1;
                }

                MessageRecord returnRecord = result.Results[0];
                returnRecord.data = strText;
                returnItem = this.ConvertMsgRecord(group, returnRecord, "browse", libId, libraryCode);
                //returnItem

                // 新创建，且check栏目的序号
                if (style == "create" && parameters != null && parameters.Contains("checkSubjectIndex") == true
                    && returnItem.subject != "")
                {
                    List<SubjectItem> list = null;
                    int nRet = this.GetSubject(libId,
                        group,
                        out list,
                        out strError);
                    if (nRet == -1)
                        goto ERROR1;
                    int nIndex = 0;
                    foreach (SubjectItem sub in list)
                    {
                        if (sub.count == 0) //因为前端不显示为0的栏目，所以这里要忽略掉
                            continue;

                        if (sub.name == returnItem.subject)
                        {
                            returnItem.subjectIndex = nIndex;
                            break;
                        }
                        nIndex++;
                    }

                    // 得到去掉序号的subject
                    int no = 0;
                    string right = returnItem.subject;
                    this.SplitSubject(returnItem.subject, out no, out right);
                    returnItem.subjectPureName = right;
                }

                return 0;
            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }
        ERROR1:
            return -1;
        }


        /// <returns>
        /// -1：出错
        /// 0   无权限
        /// 1   有权限
        /// </returns>
        public int CheckRights(WxUserItem user,
            LibEntity lib,
            string needRight,
            out string strError)
        {
            strError = "";
            string rights = user.rights;

            if (String.IsNullOrEmpty(rights) == true)
            {
                // 当发现mongodb中没有存权限信息时，从dp2library中查找
                int nRet = this.GetUserRights(lib,
                    user.userName,
                    out rights,
                    out strError);
                if (nRet == -1 || nRet == 0)
                {
                    return -1;
                }
                // 更新到mongodb中
                user.rights = rights;
                WxUserDatabase.Current.Update(user);
            }


            bool bRight = this.CheckContainRights(rights, needRight);
            if (bRight == true)
                return 1;

            return 0;
        }

        /// <summary>
        /// 检查权限字符串中是否包括指定的权限
        /// </summary>
        /// <param name="rights"></param>
        /// <param name="needRight"></param>
        /// <returns></returns>
        public bool CheckContainRights(string rights, string needRight)
        {
            if (string.IsNullOrEmpty(rights) == true || string.IsNullOrEmpty(needRight) == true)
                return false;

            // 先将rights以逗号分成数组
            string[] rightList = rights.Split(',');
            foreach (string right in rightList)
            {
                if (right == needRight)
                    return true;
            }

            return false;
        }


        /// <summary>
        /// 获取用户权限
        /// </summary>
        /// <param name="opacUserName"></param>
        /// <param name="strWord"></param>
        /// <param name="right"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public int GetUserRights(LibEntity lib,
            string strWord,
            out string rights,
            out string strError)
        {
            strError = "";
            rights = "";

            List<Record> records = null;
            int nRet = this.GetUserInfo1(lib, strWord, out records, out strError);
            if (nRet == -1 || nRet == 0)
                return nRet;

            if (records == null || records.Count == 0)
                return 0;

            string strXml = records[0].Data;
            XmlDocument dom = new XmlDocument();
            dom.LoadXml(strXml);
            rights = DomUtil.GetAttr(dom.DocumentElement, "rights");

            return 1;

        }


        public int GetUserInfo1(LibEntity lib, string strWord,
            out List<Record> records,
            out string strError)
        {
            strError = "";
            records = null;

            //long start = 0;
            //long count = 10;
            try
            {
                // 使用代理账号capo 20161024 jane
                LoginInfo loginInfo = new LoginInfo("", false);

                CancellationToken cancel_token = new CancellationToken();
                string id = Guid.NewGuid().ToString();
                SearchRequest request = new SearchRequest(id,
                    loginInfo,
                    "getUserInfo",
                    "",
                    strWord,
                    "",//strFrom,
                    "",//"middle",
                    "",//resultSet,//"weixin",
                    "",//"id,cols",
                    1,//WeiXinConst.C_Search_MaxCount,  //最大数量
                    0,  //每次获取范围
                    -1);

                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                SearchResult result = connection.SearchTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.ResultCount == -1)
                {
                    strError = "GetUserInfo()出错：" + result.ErrorInfo;//this.GetFriendlyErrorInfo(result, lib.libName, out bOffline);// result.ErrorInfo;// +" \n dp2mserver账户:" + connection.UserName;
                    return -1;
                }
                if (result.ResultCount == 0)
                {
                    strError = "未命中";
                    return 0;
                }

                records = result.Records;


                return (int)result.ResultCount;// records.Count;
            }
            catch (AggregateException ex)
            {
                strError = ex.Message;
                return -1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }

        }


        public const string C_Active_EnumSubject = "enumSubject";

        public static string checkGroup(string group)
        {
            if (group != dp2WeiXinService.C_Group_Bb
               && group != dp2WeiXinService.C_Group_Book
               && group != dp2WeiXinService.C_Group_HomePage
                && group != dp2WeiXinService.C_Group_dp_home)
            {
                return "不支持的群" + group;
            }

            return "";
        }

        /// <summary>
        /// 获取栏目
        /// </summary>
        /// <param name="weixinId"></param>
        /// <param name="libId"></param>
        /// <param name="userName">
        /// 返回的当前绑定工作人员账户号，如果为空，不没有编辑权限
        /// </param>
        /// <returns></returns>
        public int GetSubject(string libId,
            string group,
            out List<SubjectItem> list,
            out string strError)
        {
            strError = "";
            list = new List<SubjectItem>();

            strError = checkGroup(group);
            if (strError != "")
            {
                return -1;
            }

            // 获取栏目
            List<MessageRecord> records = null;
            int nRet = this.GetMessageInternal(C_Active_EnumSubject,  //enumSubject
                group,
                libId,
                "",
                "",
                out records,
                out strError);
            if (nRet == -1)
                return -1;

            foreach (MessageRecord record in records)
            {
                string[] subjects = record.subjects;
                SubjectItem subItem = new SubjectItem();

                // 发现确实空的情况。
                if (subjects == null || subjects.Length == 0)
                    continue;

                string subject = subjects[0];//2016-8-20 jane 这里的栏目是从服务器上得到的，不用管首尾空白的问题，如果管了反而暴露不出来问题

                // 2022.7.4 注释掉，一下子理解不了原来的意思
                //subject = StringUtil.UnescapeString(subject);

                int no = 0;
                string right = subject;
                this.SplitSubject(subject, out no, out right);

                subItem.no = no;
                subItem.pureName = right;
                subItem.name = subject;
                subItem.count = 0;
                try
                {
                    subItem.count = Convert.ToInt32(record.data);
                }
                catch (Exception ex)
                {
                    {
                        strError = "将data转变数字出错：" + ex.Message;
                        return -1;
                    }
                }
                list.Add(subItem);
            }

            // 如果是图书馆介绍，需要加一些默认模板
            if (group == dp2WeiXinService.C_Group_HomePage)
            {
                LibEntity lib = this.GetLibById(libId);
                string dir = dp2WeiXinService.Instance._weiXinDataDir + "/lib/" + "template/home";// +lib.capoUserName + "/home";
                if (Directory.Exists(dir) == true)
                {
                    string[] files = Directory.GetFiles(dir, "*.txt");
                    foreach (string file in files)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        bool bExist = this.checkContaint(list, fileName);
                        if (bExist == false)
                        {
                            string subject = fileName.Trim();// 2016-8-20 对首尾去空白，因为服务器对subject的空白支持不太好
                            int no = 0;
                            string right = subject;
                            this.SplitSubject(subject, out no, out right);

                            SubjectItem subItem = new SubjectItem();
                            subItem.no = no;
                            subItem.pureName = right;
                            subItem.name = subject;
                            subItem.count = 0;
                            list.Add(subItem);
                        }
                    }
                }
            }


            // 2016-8-19 jane 修改栏目排序算法

            // 先检查列表项中有没有带{}的
            bool hasParenthesis = false;
            foreach (SubjectItem sub in list)
            {
                if (sub.no != C_ExceptNo)
                {
                    hasParenthesis = true;
                    break;
                }
            }

            // 带括号的情况，括号里的值会自动扩充长度
            if (hasParenthesis == true)
            {
                list.Sort((x, y) =>
                {
                    //// 一个有括号，一个没括号，有{}的排前面
                    //if (x.no == -1 && y.no !=-1) // 左没有，右有{}
                    //    return 0;
                    //if (x.no != -1 && y.no == -1) // 左有{}，右没有
                    //    return 1;
                    if ((x.no == C_ExceptNo && y.no != C_ExceptNo) || (x.no != C_ExceptNo && y.no == C_ExceptNo))
                    {
                        // 右对齐 2016-8-20 jane 发现上面的算法，当一个有括号一个没括号时，排序出来的结果不出。
                        string tempName1 = x.name;
                        string tempName2 = y.name;
                        int length = tempName1.Length > tempName2.Length ? tempName1.Length : tempName2.Length;
                        if (tempName1.Length < length)
                            tempName1 = tempName1.PadLeft(length, '0');
                        if (tempName2.Length < length)
                            tempName2 = tempName2.PadLeft(length, '0');

                        return tempName1.CompareTo(tempName2); //左对齐排序


                    }


                    // 都没有括号的时候左对齐
                    if (x.no == C_ExceptNo && x.no == C_ExceptNo)
                        return x.name.CompareTo(y.name);


                    /*
                    //都有括号的时候，把括号里的内容扩充为等长，前补0
                    string tempNo1 = x.no.ToString();
                    string tempNo2 = y.no.ToString();
                    int maxLength = tempNo1.Length > tempNo2.Length ? tempNo1.Length : tempNo2.Length;
                    if (tempNo1.Length < maxLength)
                        tempNo1 = tempNo1.PadLeft(maxLength, '0');
                    if (tempNo2.Length < maxLength)
                        tempNo2 = tempNo2.PadLeft(maxLength, '0');

                    string name1 = "{" + tempNo1 + "}" + x.pureName;
                    string name2 = "{" + tempNo2 + "}" + y.pureName;

                    return name1.CompareTo(name2); //左对齐排序
                     */
                    // 20170314 当都有括号时，改为按括号里的数字排序
                    return x.no.CompareTo(y.no);
                });
            }
            else
            {
                // 不带括号的情况，全部左对齐
                list.Sort((x, y) =>
                {
                    int value = x.name.CompareTo(y.name);
                    return value;
                });
            }


            return records.Count;
        }

        private const int C_ExceptNo = -999999;

        public void SplitSubject(string subject, out int no, out string right)
        {
            no = C_ExceptNo;
            int nIndex = subject.IndexOf('}');
            string left = "";
            right = subject;
            if (nIndex > 0)
            {
                right = subject.Substring(nIndex + 1);
                left = subject.Substring(0, nIndex + 1);//{3}
                left = left.Substring(0, left.Length - 1);//{3
                if (left.Length > 0 && left.Substring(0, 1) == "{")
                    left = left.Substring(1).Trim(); //去掉序号两边的空格
                try
                {
                    no = Convert.ToInt32(left);
                }
                catch (Exception ex)
                {
                    this.WriteErrorLog("栏目'" + subject + "'的序号格式不合法，无法参于排序。");
                }
            }
            // 2016-8-19 排序只处理{}的情况，注掉下方内容
            //else
            //{
            //    //检查是否有空格，如果空格前面是数字，也参考排序
            //    nIndex = subject.IndexOf(' ');
            //    if (nIndex > 0)
            //    {
            //        left = subject.Substring(0, nIndex);
            //        try
            //        {
            //            no = Convert.ToInt32(left);
            //        }
            //        catch
            //        {
            //            this.WriteErrorLog("栏目'" + subject + "'虽有空格，但空格前是非数字，无法参考排序。");
            //        }
            //    }

            //}
        }

        /// <summary>
        /// 检查一个栏目是否已存在
        /// </summary>
        /// <param name="list"></param>
        /// <param name="subject"></param>
        /// <returns></returns>
        private bool checkContaint(List<SubjectItem> list, string subject)
        {
            foreach (SubjectItem item in list)
            {
                if (item.name == subject)
                    return true;
            }
            return false;
        }
        public string GetSubjectHtml(string libId, string group, string selSubject, bool bNew, List<SubjectItem> list)
        {
            int nRet = 0;
            string strError = "";
            if (list == null) //外面可以传进来
            {
                nRet = this.GetSubject(libId, group, out list, out strError);
                if (nRet == -1)
                {
                    return "获取好书推荐的栏目出错";
                }
            }
            //if (String.IsNullOrEmpty(selSubject)== false)
            //{
            //    if (selSubject.Length > 6 && selSubject.Substring(0, 6) == "msgid-")
            //    {
            //        string msgId=selSubject.Substring(6);
            //        List<MessageItem> msgList = null;
            //        nRet = this.GetMessage(group,
            //            libId,
            //            msgId,
            //            "",
            //            "original",
            //            out msgList,
            //            out strError);
            //        if (nRet ==-1)
            //        {
            //            return "获得id为'"+msgId+"'的message出错。";
            //        }

            //        if(msgList != null && msgList.Count>0)
            //        {
            //            selSubject = msgList[0].subject;
            //        }
            //    }
            //}

            var opt = "<option value=''>请选择 栏目</option>";
            for (var i = 0; i < list.Count; i++)
            {
                SubjectItem item = list[i];
                string selectedString = "";
                if (selSubject != "" && selSubject == item.name)
                {
                    selectedString = " selected='selected' ";
                }
                opt += "<option value='" + item.name + "' " + selectedString + ">" + item.name + "</option>";
            }

            string onchange = "";
            if (bNew == true)
            {
                opt += "<option value='new'>自定义栏目</option>";

                onchange = " onchange='subjectChanged(false,this)' ";

                if (group == dp2WeiXinService.C_Group_HomePage)
                    onchange = " onchange='subjectChanged(true,this)' ";
            }

            string subjectHtml = "<select id='selSubject'  " + onchange + " >" + opt + "</select>";


            return subjectHtml;
        }

        #endregion

        #region 恢复绑定账户

        public WxUserResult AddAppId_HF()
        {
            WxUserResult result = new WxUserResult();

            /*
            GzhCfg gzh=this.gzhContainer.GetDefault();

            List<UserSettingItem> settingList = UserSettingDb.Current.GetAll();
            foreach (UserSettingItem one in settingList)
            {
                string weixinId = one.weixinId;

                if (weixinId == C_Supervisor)
                    continue;

                if (weixinId.IndexOf("@") == -1)
                    weixinId += "@" + gzh.appId;

                one.weixinId = weixinId;
                UserSettingDb.Current.UpdateById(one);
            }

            List<WxUserItem> userList = WxUserDatabase.Current.GetAll();
            foreach (WxUserItem one in userList)
            {
                string weixinId = one.weixinId;
                if (weixinId == C_Supervisor)
                    continue;

                if (weixinId.IndexOf("@") == -1)
                {
                    if (string.IsNullOrEmpty(one.appId) == false)
                    {
                        weixinId += "@" + one.appId;
                    }
                    else
                    {
                        weixinId += "@" + gzh.appId;
                    }
                }

                one.weixinId = weixinId;
                WxUserDatabase.Current.Update(one);
            }

            */
            return result;
        }

        public WxUserResult RecoverUsers_HF()
        {
            string strError = "";
            WxUserResult result = new WxUserResult();

            /*
            // 统一设置一下setting表中当前用户patronRefId，用于恢复过程的最后一更，更新当前活动账户
            List<WxUserItem> activeUserList = WxUserDatabase.Current.GetActivePatrons();
            foreach (WxUserItem activeUser in activeUserList)
            {
                UpdateUserSetting(activeUser.weixinId, 
                    activeUser.libId, 
                    activeUser.bindLibraryCode,
                    null, 
                    false,
                    activeUser.refID); // todo 这里的libid有没有错
            }


            // 循环处理每个图书馆
            //List<LibEntity> libs = LibDatabase.Current.GetLibs();
            List<Library> libs = this.LibManager.Librarys;
            foreach (Library library in libs)
            {
                LibEntity lib = library.Entity;
                // 从远方图书馆查到绑定了微信的工作人员，以临时状态保存的微信用户库
                int nRet = this.SetWorkersFromLib_HF(lib,
                    out strError);
                if (nRet == -1)
                {
                    //goto ERROR1;
                    this.WriteErrorLog1("恢复用户-获得工作人员出错：" + strError);
                    continue;
                }

                // 从远方图书馆查到绑定了微信的读者，以临时状态保存的微信用户库
                long lRet = this.SetPatronsFromLib_HF(lib,
                    out strError);
                if (lRet == -1)
                {
                    //goto ERROR1;
                    this.WriteErrorLog1("恢复用户-获得读者出错：" + strError);
                    continue;
                }

                // 将原来有效的删除
                WxUserDatabase.Current.Delete(lib.id, WxUserDatabase.C_State_Available);

                // 将临时状态变为有效状态
                WxUserDatabase.Current.SetState(lib.id, WxUserDatabase.C_State_Temp, WxUserDatabase.C_State_Available);


                // 根据用户setting表，重新设上当前活动账户
                List<UserSettingItem> settingList = UserSettingDb.Current.GetByLibId(lib.id);
                foreach (UserSettingItem setting in settingList)
                {
                    if (String.IsNullOrEmpty(setting.patronRefID) == false)
                    {
                        WxUserItem tempUser = WxUserDatabase.Current.GetPatronByPatronRefID(setting.weixinId,
                            setting.libId,
                            setting.patronRefID);

                        if (tempUser != null)
                        {
                            // 把对应的绑定账户激活
                            WxUserDatabase.Current.SetActivePatron(setting.weixinId, tempUser.id);
                        }
                    }
                }
            }


            List<WxUserItem> list = WxUserDatabase.Current.Get(null, null, -1, null, null, false);//.GetUsers();
            result.users = list;
            */

            return result;

        ERROR1:
            result.errorCode = -1;
            result.errorInfo = strError;

            return result;

        }

        /// <summary>
        /// 从图书馆查询到绑定了微信的读者，同时转为mongodb的格式
        /// </summary>
        /// <param name="lib"></param>
        /// <param name="users"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        public long SetPatronsFromLib_HF(LibEntity lib,
            out string strError)
        {
            strError = "";

            // 使用代理账号capo 20161024 jane
            LoginInfo loginInfo = new LoginInfo("", false);

            // 从远程dp2library中查
            string strWord = WeiXinConst.C_WeiXinIdPrefix;// +weixinId;
            CancellationToken cancel_token = new CancellationToken();
            string id = Guid.NewGuid().ToString();
            SearchRequest request = new SearchRequest(id,
                loginInfo,
                "searchPatron",
                "<全部>",
                strWord,
                "email",
                "left",
                "wx-patron",
                "id,xml",
                1000,
                0,
                WeiXinConst.C_Search_MaxCount);
            try
            {
                MessageConnection connection = this._channels.GetConnectionTaskAsync(
                    this._dp2MServerUrl,
                    lib.capoUserName).Result;

                SearchResult result = connection.SearchTaskAsync(
                    lib.capoUserName,
                    request,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;
                if (result.ResultCount == -1)
                {
                    bool bOffline = false;
                    strError = "图书馆[" + lib.libName + "]返回错误:" + result.ErrorInfo;

                    //strError = this.GetFriendlyErrorInfo(result, lib.libName, out bOffline);// result.ErrorInfo;
                    return -1;
                }
                if (result.ResultCount == 0)
                    return 0;


                foreach (Record record in result.Records)// int i = 0; i < result.ResultCount; i++)
                {
                    string xml = record.Data;

                    List<string> weixinIds = null;
                    WxUserItem patronInfo = this.GetPatronInfoByXml(xml, out weixinIds);
                    foreach (string oneWeixinId in weixinIds)
                    {
                        WxUserItem userItem = this.newPatronUserItem(lib, oneWeixinId, patronInfo);
                        userItem.state = WxUserDatabase.C_State_Temp;

                        WxUserDatabase.Current.Add(userItem);
                    }

                    return 1;
                }

            }
            catch (AggregateException ex)
            {
                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }
        ERROR1:
            return -1;
        }

        public int SetWorkersFromLib_HF(LibEntity lib,
    out string strError)
        {
            strError = "";

            List<Record> records = null;
            int nRet = this.GetUserInfo1(lib, "", out records, out strError);
            if (nRet == -1 || nRet == 0)
            {
                return nRet;
            }

            foreach (Record record in records)// int i = 0; i < result.ResultCount; i++)
            {
                string xml = record.Data;

                List<string> weixinIds = null;
                string name = "";
                string libraryCode = "";
                string rights = "";
                this.GetWorkerInfoByXml(xml, out weixinIds, out name, out libraryCode, out rights);
                foreach (string oneWeixinId in weixinIds)
                {
                    WxUserItem userItem = this.NewWorkerUserItem(lib, oneWeixinId, name, libraryCode, rights);
                    userItem.state = WxUserDatabase.C_State_Temp;// 设为临时状态
                    WxUserDatabase.Current.Add(userItem);
                }

            }




            return 1;
        }

        #endregion

        #region 通用函数

        // todo
        public WxUserItem newPatronUserItem(LibEntity lib,
            string weixinId,
            WxUserItem patronInfo)
        {
            WxUserItem userItem = new WxUserItem();
            userItem.weixinId = weixinId;
            userItem.libName = lib.libName;
            userItem.libId = lib.id;
            userItem.bindLibraryCode = patronInfo.libraryCode; ;//初始设置为实际分馆
            if (string.IsNullOrEmpty(userItem.bindLibraryCode) == false)
                userItem.libName = userItem.bindLibraryCode;

            userItem.readerBarcode = patronInfo.readerBarcode;
            userItem.readerName = patronInfo.readerName;
            userItem.gender = patronInfo.gender;
            userItem.department = patronInfo.department;
            userItem.phone = patronInfo.phone;
            userItem.gender = patronInfo.gender;
            userItem.patronState = patronInfo.patronState;
            userItem.noPassReason = patronInfo.noPassReason;
            userItem.xml = patronInfo.xml;

            userItem.isRegister = false;

            userItem.refID = patronInfo.refID;
            userItem.createTime = DateTimeUtil.DateTimeToString(DateTime.Now);
            userItem.updateTime = userItem.createTime;
            userItem.isActive = 0;  // 是否为活动状态

            userItem.libraryCode = patronInfo.libraryCode;
            userItem.type = WxUserDatabase.C_Type_Patron;
            userItem.userName = "";
            userItem.isActiveWorker = 0;//20220720该字段已作废。//是否是激活的工作人员账户，读者时均为0
            userItem.tracing = "off";//无意义，设为关闭状态


            userItem.state = WxUserDatabase.C_State_Available;
            userItem.remark = "";
            userItem.rights = patronInfo.rights;
            userItem.appId = patronInfo.appId;

            // 馆藏地 2017-2-14 加
            userItem.location = patronInfo.location;
            userItem.selLocation = "";

            // 2017-4-19
            userItem.verifyBarcode = 0;
            userItem.audioType = 4; // 2018/1/2

            return userItem;
        }


        // todo
        public WxUserItem NewWorkerUserItem(LibEntity lib,
            string weixinId,
            string name,
            string libraryCode,
            string rights)
        {
            WxUserItem userItem = new WxUserItem();
            userItem.weixinId = weixinId;
            userItem.libName = lib.libName;
            userItem.libId = lib.id;
            userItem.bindLibraryCode = libraryCode; //初始设置为实际分馆
            if (string.IsNullOrEmpty(userItem.bindLibraryCode) == false)
            {
                userItem.libName = userItem.bindLibraryCode;
            }


            userItem.readerBarcode = "";
            userItem.readerName = "";
            userItem.department = "";
            userItem.phone = "";
            userItem.gender = "";
            userItem.patronState = "";
            userItem.noPassReason = "";
            userItem.xml = "";

            userItem.isRegister = false;

            userItem.refID = "";
            userItem.createTime = DateTimeUtil.DateTimeToString(DateTime.Now);
            userItem.updateTime = userItem.createTime;
            userItem.isActive = 0;   //是否为活动状态

            userItem.libraryCode = libraryCode; //实际分馆
            userItem.type = WxUserDatabase.C_Type_Worker;
            userItem.userName = name;
            userItem.isActiveWorker = 0;//20220720该字段已作废。//是否是激活的工作人员账户，读者时均为0
            userItem.tracing = "off";//默认是关闭状态

            userItem.state = WxUserDatabase.C_State_Available;
            userItem.remark = "";
            userItem.rights = rights;

            return userItem;
        }

        /// <summary>
        /// 根据绑定接口返回的xml，解析出读者帐户的信息
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="weixinIds"></param>
        /// <returns></returns>
        private WxUserItem GetPatronInfoByXml(string xml, out List<string> weixinIds)
        {
            weixinIds = new List<string>();

            XmlDocument dom = new XmlDocument();
            dom.LoadXml(xml);
            XmlNode root = dom.DocumentElement;
            XmlNode node = null;

            // 证条码号
            string readerBarcode = "";
            node = root.SelectSingleNode("barcode");
            if (node != null)
                readerBarcode = DomUtil.GetNodeText(node);

            // 姓名
            string readerName = "";
            node = root.SelectSingleNode("name");
            if (node != null)
                readerName = DomUtil.GetNodeText(node);


            //参考id
            string refID = "";
            node = root.SelectSingleNode("refID");
            if (node != null)
                refID = DomUtil.GetNodeText(node);

            // 部门
            string department = "";
            node = root.SelectSingleNode("department");
            if (node != null)
                department = DomUtil.GetNodeText(node);

            // 电话
            string phone = "";
            node = root.SelectSingleNode("tel");
            if (node != null)
                phone = DomUtil.GetNodeText(node);

            // gender
            string gender = "";
            node = root.SelectSingleNode("gender");
            if (node != null)
                gender = DomUtil.GetNodeText(node);

            // 读者状态
            string patronState = "";
            node = root.SelectSingleNode("state");
            if (node != null)
                patronState = DomUtil.GetNodeText(node);

            // 备注
            string noPassReason = "";
            node = root.SelectSingleNode("comment");
            if (node != null)
                noPassReason = DomUtil.GetNodeText(node);


            // 分馆代码
            string libraryCode = "";
            node = root.SelectSingleNode("libraryCode");
            if (node != null)
                libraryCode = DomUtil.GetNodeText(node);

            // 权限
            string rights = "";
            node = root.SelectSingleNode("rights");
            if (node != null)
                rights = DomUtil.GetNodeText(node);

            // 馆藏地 2017-2-14 加
            string locXml = "";
            string personalLibrary = "";
            node = root.SelectSingleNode("personalLibrary"); // 书斋名称
            if (node != null)
                personalLibrary = DomUtil.GetNodeText(node);

            if (personalLibrary == "*")  //*表示全部
                personalLibrary = "";

            if (personalLibrary != "")
            {
                /*
<library code="星洲小学">
    <item canborrow="yes" itemBarcodeNullable="yes">阅览室</item>
</library>
                 */
                // 将管理的馆藏地组成这种格式。
                string[] locations = personalLibrary.Split(new char[] { ',' });
                foreach (string loc in locations)
                {
                    locXml += "<item>" + loc + "</item>";
                }

                if (locXml != "")
                {
                    locXml = "<library code='" + libraryCode + "'>"
                        + locXml
                        + "</library>";
                }
            }


            WxUserItem userItem = new WxUserItem();

            // 2020-3-7,现在已经没有证条码号为空的情况了
            // 如果barcode为空，则用"@refid:" + refID 来代替
            if (readerBarcode == null)
                readerBarcode = "";
            if (readerBarcode == "")
                readerBarcode = "@refid:" + refID;

            userItem.readerBarcode = readerBarcode;
            userItem.readerName = readerName;
            userItem.department = department;
            userItem.phone = phone;
            userItem.gender = gender;
            userItem.patronState = patronState;
            userItem.noPassReason = noPassReason;
            userItem.refID = refID;
            userItem.libraryCode = libraryCode;
            userItem.xml = xml;
            userItem.rights = rights;
            userItem.location = locXml; // 馆藏地 2017-2-14 加

            // 取email
            string email = "";
            node = root.SelectSingleNode("email");
            if (node != null)
                email = DomUtil.GetNodeText(node);
            weixinIds = this.GetWeixinIds(email);

            return userItem;
        }

        /// <summary>
        /// 从bind接口返回的xml解析出工作人员信息
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="weixinIds"></param>
        /// <param name="name"></param>
        /// <param name="libraryCode"></param>
        /// <param name="rights"></param>
        private void GetWorkerInfoByXml(string xml, out List<string> weixinIds,
            out string name,
            out string libraryCode,
            out string rights)
        {
            weixinIds = new List<string>();

            XmlDocument dom = new XmlDocument();
            dom.LoadXml(xml);
            XmlNode root = dom.DocumentElement;

            name = DomUtil.GetAttr(root, "name");
            libraryCode = DomUtil.GetAttr(root, "libraryCode");
            rights = DomUtil.GetAttr(root, "rights");

            // 取出binding="weixinid:o4xvUviTxj2HbRqbQb9W2nMl4fGg" 
            string binding = DomUtil.GetAttr(root, "binding");
            weixinIds = this.GetWeixinIds(binding);
        }

        private List<string> GetWeixinIds(string text)
        {
            List<string> weixinIds = new List<string>();

            GzhCfg gzh = this._gzhContainer.GetDefault();

            if (String.IsNullOrEmpty(text) == false)
            {
                string[] emailList = text.Split(new char[] { ',' });
                for (int i = 0; i < emailList.Length; i++)
                {
                    string oneEmail = emailList[i].Trim();
                    if (oneEmail.Length > 9 && oneEmail.Substring(0, 9) == WeiXinConst.C_WeiXinIdPrefix)
                    {
                        string weixinId = oneEmail.Substring(9).Trim();
                        if (weixinId != "")
                        {
                            //int nTemp = weixinId.IndexOf("@");
                            //if (nTemp == -1)
                            //    weixinId += "@" + gzh.appId;

                            weixinIds.Add(weixinId);
                        }
                    }
                }
            }
            return weixinIds;

        }

        private List<string> AddAppIdForWeixinId(List<string> weixinIds)
        {
            GzhCfg gzh = dp2WeiXinService.Instance._gzhContainer.GetDefault();
            for (int i = 0; i < weixinIds.Count; i++)
            {
                string weixinId = weixinIds[i];
                weixinIds[i] = this.AddAppIdForWeixinId(weixinId, gzh);
            }
            return weixinIds;
        }

        public string AddAppIdForWeixinId(string weixinId, GzhCfg gzh)
        {
            if (gzh == null)
                gzh = dp2WeiXinService.Instance._gzhContainer.GetDefault();

            // 2020-3-7 注意这里还是检查web来源的绑定，~~开头但没有@，web来源不需要加@公众号appid
            if (WxUserDatabase.CheckFrom(weixinId) == WxUserDatabase.C_from_web
                || WxUserDatabase.CheckFrom(weixinId)==WxUserDatabase.C_from_applet)
                return weixinId;

            // 很久之前的版本，weixinid没有带公众号后缀，所以这里自动处理一下
            if (weixinId.IndexOf('@') == -1)
            {
                return weixinId += "@" + gzh.appId;
            }

            return weixinId;
        }


        /// <summary>
        /// 根据dp2传来的帐户变更修改mongodb对应记录的信息，只处理change的帐户，且只处理dp2与mongodb两边同时存在的weixinId
        /// </summary>
        /// <param name="lib"></param>
        /// <param name="bodyDom"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        private int UpdateWorker(LibEntity lib, XmlDocument bodyDom, out string strError)
        {
            strError = "";

            //<root>
            //  <type>工作人员账户变动</type>
            //  <operation>setUser</operation>
            //  <action>change</action>
            //  <operator>jane</operator>
            //  <operTime>Fri, 23 Sep 2016 03:37:05 +0800</operTime>
            //  <oldAccount name="jane" rights="borrow,return,renew,lost,read,reservation,order,setclock,changereaderpassword,verifyreaderpassword,getbibliosummary,searchcharging,searchreader,getreaderinfo,setreaderinfo,movereaderinfo,changereaderstate,changereaderbarcode,listbibliodbfroms,listdbfroms,searchbiblio,getbiblioinfo,searchitem,getiteminfo,setiteminfo,getoperlog,amerce,amercemodifyprice,amercemodifycomment,amerceundo,inventory,inventorydelete,search,getrecord,getcalendar,changecalendar,newcalendar,deletecalendar,batchtask,clearalldbs,devolvereaderinfo,getuser,changeuser,newuser,deleteuser,changeuserpassword,simulatereader,simulateworker,getsystemparameter,setsystemparameter,urgentrecover,repairborrowinfo,passgate,getres,writeres,setbiblioinfo,hire,foregift,returnforegift,settlement,undosettlement,deletesettlement,searchissue,getissueinfo,setissueinfo,searchorder,getorderinfo,setorderinfo,getcommentinfo,setcommentinfo,searchcomment,denychangemypassword,writeobject,writerecord,writetemplate,managedatabase,restore,managecache,managecomment,settailnumber,setutilinfo,getpatrontempid,getchannelinfo,managechannel,viewreport,upload,download,checkclientversion,client_uimodifyorderrecord,client_forceverifydata,client_deletebibliosubrecords,client_simulateborrow,bindpatron,resetpasswordreturnmessage,_wx_setbb,_wx_setbook,_wx_setHomePage,_wx_getWarning" libraryCode="" access="" comment="" type="" binding="weixinid:o4xvUviTxj2HbRqbQb9W2nMl4fGg" />
            //  <account name="jane" rights="borrow,return,renew,lost,read,reservation,order,setclock,changereaderpassword,verifyreaderpassword,getbibliosummary,searchcharging,searchreader,getreaderinfo,setreaderinfo,movereaderinfo,changereaderstate,changereaderbarcode,listbibliodbfroms,listdbfroms,searchbiblio,getbiblioinfo,searchitem,getiteminfo,setiteminfo,getoperlog,amerce,amercemodifyprice,amercemodifycomment,amerceundo,inventory,inventorydelete,search,getrecord,getcalendar,changecalendar,newcalendar,deletecalendar,batchtask,clearalldbs,devolvereaderinfo,getuser,changeuser,newuser,deleteuser,changeuserpassword,simulatereader,simulateworker,getsystemparameter,setsystemparameter,urgentrecover,repairborrowinfo,passgate,getres,writeres,setbiblioinfo,hire,foregift,returnforegift,settlement,undosettlement,deletesettlement,searchissue,getissueinfo,setissueinfo,searchorder,getorderinfo,setorderinfo,getcommentinfo,setcommentinfo,searchcomment,denychangemypassword,writeobject,writerecord,writetemplate,managedatabase,restore,managecache,managecomment,settailnumber,setutilinfo,getpatrontempid,getchannelinfo,managechannel,viewreport,upload,download,checkclientversion,client_uimodifyorderrecord,client_forceverifydata,client_deletebibliosubrecords,client_simulateborrow,bindpatron,resetpasswordreturnmessage,_wx_setbb,_wx_setbook,_wx_setHomePage,_wx_getWarning," libraryCode="" access="" comment="" type="" binding="weixinid:o4xvUviTxj2HbRqbQb9W2nMl4fGg" />
            //  <clientAddress via="net.pipe://localhost/dp2library/XE">::1</clientAddress>
            //  <version>1.02</version>
            //</root>

            // 取出传过来的账户信息
            XmlNode root = bodyDom.DocumentElement;

            XmlNode actionNode = root.SelectSingleNode("action");
            string action = DomUtil.GetNodeText(actionNode);
            //  不是change的情况，不用处理
            if (action != "change")
                return 0;

            XmlNode nodeAccount = root.SelectSingleNode("account");

            List<string> dp2WeixinIds = null;
            string name = "";
            string libraryCode = "";
            string rights = "";
            this.GetWorkerInfoByXml(nodeAccount.OuterXml,
                out dp2WeixinIds,
                out name,
                out libraryCode,
                out rights);
            if (name == "")
            {
                this.WriteErrorLog("服务器传来的 工作人员账户变动 消息的账号名为空");
                return 0;
            }

            //2016-11-16,处理早期版本绑定的帐户，weixinid没有后缀的情况
            dp2WeixinIds = AddAppIdForWeixinId(dp2WeixinIds);

            this.WriteDebug("收到通知，更新图书馆 " + lib.libName + " 工作人员 " + name + " 的信息。");

            // 查一下数据库中有没有绑定该账户的微信
            List<WxUserItem> userList = WxUserDatabase.Current.GetWorkers(null, lib.id, name);

            //  2020-3-7 不使用下面比对两者weixinId分出3批数据的做法，
            // 修改为直接根据变更帐户修改mongodb库的中该帐户对应所有记录的信息，
            // 不论dp2端的weixinId是否与mongodb一致,都不会根据dp2中的weixinId处理公众号mongodb的记录
            foreach (WxUserItem userItem in userList)
            {
                //string weixinId = this.AddAppIdForWeixinId(userItem.weixinId, null);
                //userItem.weixinId = weixinId;
                userItem.libraryCode = libraryCode;
                userItem.rights = rights;
                userItem.updateTime = DateTimeUtil.DateTimeToString(DateTime.Now);
                WxUserDatabase.Current.Update(userItem);
            }


            /*
            List<string> mongoWeixinIds = new List<string>();
            foreach (WxUserItem user in userList)
            {
                mongoWeixinIds.Add(user.weixinId);
            }
            mongoWeixinIds = AddAppIdForWeixinId(mongoWeixinIds);

            // 没有绑定的微信用户
            if (dp2WeixinIds.Count == 0 && mongoWeixinIds.Count == 0)
                return 0;

            List<string> addIds = null;
            List<string> modifyIds = null;
            List<string> deleteIds = null;
            this.CompareOldNew(dp2WeixinIds, 
                mongoWeixinIds,
                out addIds, out modifyIds, out deleteIds);

            // 2020-3-7 修改为只处理dp2与mongodb同时存在的帐户，然后更新帐户的信息。
            // 对于dp2端新增的weixinid不做处理，因为不能确保dp2增加的weixinid是否正确，
            // 所以不能单方面在dp2的读者email加weixinId,公众号这边不认
            // 对于dp2端从email删除了weixinid，公众号这边也不认，mongodb还依然是原样存储
            // 删除的不做处理有3个主要原因：
            // 1）删除公众号端信息涉及到用户的体验，本来在微信用的好好的，忽略绑定帐户不存在
            // 2) 删除还涉及到当前活动帐户，如果可好删除是当前活动帐户，
            // 以前的做法时，删除的如果是当前活动帐户则自动找一个绑定帐户，又不是用户自己操作了，也影响用户体现，会让用户莫名其妙
            // 3）对于读者记录变更，也仅处理的是change，未处理delete，所以既然dp2删除一条读者记录都不做处理，
            // 那么更没必要因为email中weixinId删了，就从公众号的mongodb库中删除。

            // 修改的用户
            if (modifyIds.Count > 0)
            {
                foreach (string weixinId in modifyIds)
                {
                    WxUserItem userItem = this.getUser(userList, weixinId);
                    userItem.libraryCode = libraryCode;
                    userItem.rights = rights;
                    userItem.updateTime = DateTimeUtil.DateTimeToString(DateTime.Now);
                    WxUserDatabase.Current.Update(userItem);
                }
            }

            //// 新增的绑定用户
            //if (addIds.Count > 0)
            //{
            //    foreach (string weixinId in addIds)
            //    {
            //        WxUserItem userItem = this.NewWorkerUserItem(lib, weixinId, name, libraryCode, rights);
            //        WxUserDatabase.Current.Add(userItem);
            //    }
            //}
            //// 删除的用户
            //if (deleteIds.Count > 0)
            //{
            //    foreach (string weixinId in deleteIds)
            //    {
            //        WxUserItem userItem = this.getUser(userList, weixinId);
            //        WxUserDatabase.Current.Delete(userItem.id, -1); 
            //    }
            //}
            */

            return 1;
        }

        private WxUserItem getUser(List<WxUserItem> users, string weixinId)
        {
            foreach (WxUserItem user in users)
            {
                if (user.weixinId == weixinId)
                    return user;
            }
            return null;
        }

        public void CompareOldNew(List<string> oldWeixinIds,
            List<string> newWeixinIds,
            out List<string> addIds,
            out List<string> modifyIds,
            out List<string> deleteIds)
        {
            addIds = new List<string>();
            modifyIds = new List<string>();
            deleteIds = new List<string>();

            // 检查出删除和修改的
            foreach (string id in oldWeixinIds)
            {
                if (newWeixinIds.Contains(id) == false)
                {
                    deleteIds.Add(id);
                }
                else
                {
                    modifyIds.Add(id);
                }
            }

            // 检查出新增的
            foreach (string id in newWeixinIds)
            {
                if (oldWeixinIds.Contains(id) == false)
                    addIds.Add(id);
            }
        }

        /// <summary>
        /// 读者信息更新
        /// </summary>
        /// <param name="lib"></param>
        /// <param name="bodyDom"></param>
        /// <param name="strError"></param>
        /// <returns></returns>
        private int UpdatePatron(LibEntity lib, XmlDocument bodyDom, out string strError)
        {
            strError = "";
            int nRet = 0;

            //<root>
            //    <type>读者记录变动</type>
            //    <operation>setReaderInfo</operation>
            //    <libraryCode></libraryCode>
            //    <action>change</action>
            //    <operator>jane</operator>
            //    <operTime>Fri, 23 Sep 2016 05:20:28 +0800</operTime>
            //    <clientAddress>::1</clientAddress>
            //    <version>1.02</version>
            //    <patronRecord>
            //      <barcode>R00003</barcode>
            //      <readerType>教职工</readerType>
            //      <name>任3-1</name>
            //      <refID>63aeb890-8936-4471-bfc5-8e72d5c7fe94</refID>
            //      <tel>13862157150</tel>
            //      <hire expireDate="" period=""></hire>
            //      <email>renyh@163.com,weixinid:o4xvUviTxj2HbRqbQb9W2nMl4fGg</email>
            //      <reservations>
            //        <request items="C102" requestDate="Wed, 07 Sep 2016 16:41:57 +0800" operator="supervisor" />
            //      </reservations>
            //      <outofReservations count="4">
            //        <request itemBarcode="C104" notifyDate="Tue, 05 Jul 2016 14:29:52 +0800"></request>
            //        <request itemBarcode="C103" notifyDate="Tue, 05 Jul 2016 14:40:50 +0800"></request>
            //        <request itemBarcode="C102" notifyDate="Tue, 05 Jul 2016 16:27:28 +0800"></request>
            //        <request itemBarcode="C101" notifyDate="Sat, 17 Sep 2016 22:02:13 +0800" />
            //      </outofReservations>
            //      <borrows>
            //        <borrow barcode="C100" recPath="中文图书实体/4" biblioRecPath="中文图书/3" borrowDate="Sun, 04 Sep 2016 09:31:23 +0800" borrowPeriod="31day" returningDate="Wed, 05 Oct 2016 12:00:00 +0800" operator="jane" type="普通" price="CNY27.80" notifyHistory="nnyn" />
            //      </borrows>
            //      <overdues></overdues>
            //      <libraryCode></libraryCode>
            //      <dprms:file id="0" usage="cardphoto" xmlns:dprms="http://dp2003.com/dprms" />
            //    </patronRecord>
            //  </root>


            // 检查变化类型
            XmlNode root = bodyDom.DocumentElement;
            XmlNode actionNode = root.SelectSingleNode("action");
            string action = DomUtil.GetNodeText(actionNode);
            // 如果不是修改信息，则不用做什么事情
            if (action != "change") // && action!="delete")
                return 0;

            XmlNode patronRecord = root.SelectSingleNode("patronRecord");

            // 得到dp2存储weixinid
            List<string> dp2WeixinIds = null;
            WxUserItem patronInfo = this.GetPatronInfoByXml(patronRecord.OuterXml,
                out dp2WeixinIds);
            //dp2WeixinIds = this.AddAppIdForWeixinId(dp2WeixinIds);// 2016-11-16

            // 获取数据库中存储的该读者的微信
            // 2020-3-7 改为传读者refId，因为对于读者自己注册的情况，馆员审核后是新证条码号，
            // 所以不能用证条码检索
            List<WxUserItem> userList = WxUserDatabase.Current.GetPatron(null,
                lib.id,
                WxUserDatabase.C_Prefix_RefId + patronInfo.refID); //patronInfo.readerBarcode);

            // 2020/6/1 试验通过delete消息，希望删除公众号本地数据，
            // 发现delete传过来的信息非常少，没办法进一步处理，所以还是在具体的功能地方删除本地数据。
            // 如果dp2端删除了读者记录，公众号这端也跟着删除
            //if (action == "delete" && userList !=null)
            //{
            //    foreach (WxUserItem userItem in userList)
            //    {
            //        // 删除mongodb库的记录
            //        WxUserDatabase.Current.Delete1(userItem.id, out WxUserItem newActiveUser);
            //    }
            //    return 0;
            //}


            //  2020-3-7 不使用下面比对两者weixinId分出3批数据的做法，
            // 修改为直接根据变更帐户修改mongodb库的中该帐户对应所有记录的信息，
            // 不论dp2端的weixinId是否与mongodb一致,都不会根据dp2中的weixinId处理公众号mongodb的记录
            foreach (WxUserItem userItem in userList)
            {
                // 2020/6/3 将发审核结果的通知移到SetReaderInfo函数里，这样代码逻辑更紧凑些
                //// 判断mongo库的读者是否是 待审核
                //int nPass = -1;
                //if (userItem.patronState == WxUserDatabase.C_PatronState_TodoReview)
                //{
                //    if (patronInfo.patronState == WxUserDatabase.C_PatronState_Pass)
                //        nPass = 1;
                //    else if (patronInfo.patronState == WxUserDatabase.C_PatronState_NoPass)
                //        nPass = 0;
                //}


                //string weixinId = this.AddAppIdForWeixinId(user.weixinId,null);
                //user.weixinId = weixinId;

                userItem.readerBarcode = patronInfo.readerBarcode;
                userItem.readerName = patronInfo.readerName;
                userItem.department = patronInfo.department;
                userItem.phone = patronInfo.phone;
                userItem.gender = patronInfo.gender;
                userItem.patronState = patronInfo.patronState;  //更新为dp2读者记录的最新状态
                userItem.noPassReason = patronInfo.noPassReason;//更新不通过原因
                userItem.xml = patronInfo.xml;
                userItem.refID = patronInfo.refID;
                userItem.libraryCode = patronInfo.libraryCode;
                userItem.rights = patronInfo.rights;
                userItem.updateTime = DateTimeUtil.DateTimeToString(DateTime.Now);

                // 2017-2-14
                userItem.location = patronInfo.location;
                //user.selLocation = "";  //选择的location不用变更

                // 不用修改isRegister字段

                // 直接更新库中记录，不涉及到更新其它状态。
                WxUserDatabase.Current.Update(userItem);

                // 2020/6/3 将发审核结果的通知移到SetReaderInfo函数里，这样代码逻辑更紧凑些
                //// 如果是微信入口，且是审核引起的读者信息变化，则给注册用户发通知
                //if ((nPass == 0 || nPass == 1))
                ////&&(WxUserDatabase.CheckIsFromWeb(userItem.weixinId)==false)  
                //{

                //    //尊敬的用户：您好！您提交的书柜帐户注册信息已审核完成。
                //    //申请人：张三
                //    //手机号码：13866668888
                //    //审核结果：通过
                //    //您现在使用智能书柜借书了。

                //    string strFirst = "尊敬的用户：您好！您提交的书柜帐户注册信息已审核完成。";
                //    string strRemark = "您现在可以使用智能书柜借书了。";

                //    string result = "通过";
                //    if (nPass == 0)
                //    {
                //        result = "不通过";
                //        strRemark = "审核不通过原因：" + userItem.noPassReason;
                //    }

                //    // todo，这里使用的default公众号，后面如果有些单位用自己的公众号再改进
                //    GzhCfg gzh = dp2WeiXinService.Instance._gzhContainer.GetDefault();//.GetByAppId(this.AppId);
                //    if (gzh == null)
                //    {
                //        strError = "未找到默认的公众号配置";
                //        return -1;
                //    }

                //    // 到我的信息
                //    string linkUrl = "";//dp2WeiXinService.Instance.OAuth2_Url_AccountIndex,//详情转到账户管理界面
                //    linkUrl = dp2WeiXinService.Instance.GetOAuth2Url(gzh,
                //        "Patron/PersonalInfo");


                //    //// 本人
                //    //List<string> bindWeixinIds = new List<string>();
                //    //bindWeixinIds.Add(userItem.weixinId);
                //    List<WxUserItem> bindPatronList = new List<WxUserItem>();
                //    bindPatronList.Add(userItem);

                //    // 2020-3-8 改为不给工作人员发消息，怕把管理员弄混了。
                //    // 工作人员
                //    List<WxUserItem> workers = new List<WxUserItem>(); //this.GetTraceUsers(userItem.libId, userItem.libraryCode);

                //    if (bindPatronList.Count > 0 || workers.Count > 0)
                //    {
                //        // 不加mask的通知数据
                //        string thisTime = dp2WeiXinService.GetNowTime();
                //        string first_color = "#000000";
                //        ReviewResultTemplateData msgData = new ReviewResultTemplateData(strFirst, first_color,
                //            userItem.readerName, userItem.phone, result,
                //            strRemark);

                //        ////加mask的通知数据
                //        //strAccount = this.GetFullPatronName(userItem.readerName, userItem.readerBarcode, "", "", true);//this.markString(userItem.readerName) + " " + this.markString(userItem.readerBarcode) + "";
                //        //ReviewPatronTemplateData maskMsgData = new ReviewPatronTemplateData(strFirst, first_color,
                //        //    strAccount, userItem.phone, "等待审核", thisTime,
                //        //    strRemark);

                //        // 发送消息
                //        nRet = this.SendTemplateMsg(GzhCfg.C_Template_ReviewResult,
                //           bindPatronList,
                //           workers,
                //           msgData,
                //           msgData, // todo 没有做马赛克，看看后面是否需要
                //           linkUrl,
                //           "",
                //           out strError);
                //        if (nRet == -1)
                //        {
                //            return -1;
                //        }
                //    }
                //}
            }


            /*
            List<string> mongoWeixinIds = new List<string>();
            foreach (WxUserItem user in userList)
            {
                mongoWeixinIds.Add(user.weixinId);
            }
            mongoWeixinIds = this.AddAppIdForWeixinId(dp2WeixinIds);

            // dp2读者记录里没有weixinId，微信本地库也没有该读者的绑定帐户，则不用做处理
            if (dp2WeixinIds.Count == 0 && mongoWeixinIds.Count == 0)
                return 0;

            List<string> addIds = null;
            List<string> modifyIds = null;
            List<string> deleteIds = null;
            // 比对两组数据，分出新增的，修改的，删除的
            this.CompareOldNew(dp2WeixinIds, mongoWeixinIds,
                out addIds,
                out modifyIds,
                out deleteIds);

            // 2020-3-7 修改为只处理dp2与mongodb同时存在的帐户，然后更新帐户的信息。
            // 对于dp2端新增的weixinid不做处理，因为不能确保dp2增加的weixinid是否正确，
            // 所以不能单方面在dp2的读者email加weixinId,公众号这边不认
            // 对于dp2端从email删除了weixinid，公众号这边也不认，mongodb还依然是原样存储
            // 删除的不做处理有3个主要原因：
            // 1）删除公众号端信息涉及到用户的体验，本来在微信用的好好的，忽略绑定帐户不存在
            // 2) 删除还涉及到当前活动帐户，如果可好删除是当前活动帐户，
            // 以前的做法时，删除的如果是当前活动帐户则自动找一个绑定帐户，又不是用户自己操作了，也影响用户体现，会让用户莫名其妙
            // 3）对于读者记录变更，也仅处理的是change，未处理delete，所以既然dp2删除一条读者记录都不做处理，
            // 那么更没必要因为email中weixinId删了，就从公众号的mongodb库中删除。


            // 修改的用户
            foreach (string weixinId in modifyIds)
            {
                WxUserItem userItem = this.getUser(userList, weixinId);
                userItem.readerBarcode = patronInfo.readerBarcode;
                userItem.readerName = patronInfo.readerName;
                userItem.department = patronInfo.department;
                userItem.xml = patronInfo.xml;
                userItem.refID = patronInfo.refID;
                userItem.libraryCode = patronInfo.libraryCode;
                userItem.rights = patronInfo.rights;
                userItem.updateTime = DateTimeUtil.DateTimeToString(DateTime.Now);

                // 2017-2-14
                userItem.location = patronInfo.location;
                userItem.selLocation = "";

                // 2017-4-19
                userItem.verifyBarcode = 0;
                userItem.audioType = 4; // 2018/1/2

                WxUserDatabase.Current.Update(userItem);
            }

            // 新增的绑定用户
            foreach (string weixinId in addIds)
            {
                WxUserItem userItem = this.newPatronUserItem(lib, weixinId, patronInfo);
                WxUserDatabase.Current.Add(userItem);
            }
            // 删除的用户
            foreach (string weixinId in deleteIds)
            {
                WxUserItem userItem = this.getUser(userList, weixinId);
                WxUserDatabase.Current.Delete(userItem.id, -1);
            }
            */

            return 1;
        }


        #endregion

        #region 图书馆管理

        public int AddLib(LibEntity item, out LibEntity outputItem, out string strError)
        {
            strError = "";
            outputItem = null;
            try
            {
                outputItem = LibDatabase.Current.Add(item);

                ////创建对应的图书馆介绍配置目录
                //string libDir = dp2WeiXinService.Instance.weiXinDataDir + "/lib/" + item.capoUserName + "/home";
                //if (Directory.Exists(libDir) == false)
                //    Directory.CreateDirectory(libDir);
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }

            return 0;

        }


        public ApiResult deleteLib(string id)
        {
            string strError = "";

            ApiResult result = new ApiResult();
            // 先检查一下，是否有微信用户绑定了该图书馆
            List<WxUserItem> list = WxUserDatabase.Current.Get(null, id, -1);
            if (list != null && list.Count > 0)
            {

                //// 先删除绑定的用户，因为用户是与图书馆id关联的，删除了图书馆，那绑定的用户也没有意义了。
                //foreach (WxUserItem u in list)
                //{
                //    WxUserDatabase.Current.SimpleDelete(u.id);
                //}

                bool hasBind = false;
                foreach (WxUserItem item in list)
                {
                    if (item.userName != "public")
                    {
                        hasBind = true;
                        break;
                    }
                }

                if (hasBind == true)
                {
                    strError = "不能删除图书馆:目前存在" + list.Count + "个微信用户绑定，第一个名称为" + list[0].readerName + list[0].userName;
                    goto ERROR1;
                }

                // 2021/8/14
                // 先删除检索时自动产生的public用户，因为用户是与图书馆id关联的，删除了图书馆，那绑定的用户也没有意义了。
                foreach (WxUserItem u in list)
                {
                    WxUserDatabase.Current.SimpleDelete(u.id);
                }
            }

            //// 检查是否有微信用户设置了该图书馆
            //List<UserSettingItem> settingList = UserSettingDb.Current.GetByLibId(id);
            //if (settingList != null && settingList.Count > 0)
            //{
            //    //strError = "目前已经存在微信用户设置了该图书馆，不能删除图书馆。";
            //    //goto ERROR1;
            //}

            // 删除配置目录
            LibEntity lib = this.GetLibById(id);
            if (lib != null)
            {
                try
                {
                    //创建对应的图书馆介绍配置目录
                    string libDir = dp2WeiXinService.Instance._weiXinDataDir + "/lib/" + lib.capoUserName;// +"/home";
                    if (Directory.Exists(libDir) == true)
                        Directory.Delete(libDir, true);
                }
                catch (Exception ex)
                {
                    strError = ex.Message;

                    // 2016-8-19权限不够，删除不了目录的话，改为记到日志里。
                    this.WriteErrorLog(strError);
                    //goto ERROR1;
                }
            }

            // 从mongodb中删除
            LibDatabase.Current.Delete(id);
            this.LibManager.DeleteLib(id);

            // 从地区清单中删除
            if (lib != null)
            {
                this._areaMgr.DelLib(id, lib.libName);
                this._areaMgr.Save2Xml();
            }

            return result;


        ERROR1:
            result.errorCode = -1;
            result.errorInfo = strError;
            return result;
        }

        #endregion


        #region 资源下载

        public static Uri GetUri(string strURI)
        {
            try
            {
                return new Uri(strURI);
            }
            catch
            {
                return null;
            }
        }

        public static bool MatchMIME(string strMime, string strLeftParam)
        {
            string strLeft = "";
            string strRight = "";
            StringUtil.ParseTwoPart(strMime, "/", out strLeft, out strRight);
            if (string.Compare(strLeft, strLeftParam, true) == 0)
                return true;
            return false;
        }

        public static string PureName(string strPath)
        {
            // 2012/11/30
            if (string.IsNullOrEmpty(strPath) == true)
                return strPath;

            string sResult = "";
            sResult = strPath;
            sResult = sResult.Replace("/", "\\");
            if (sResult.Length > 0)
            {
                if (sResult[sResult.Length - 1] == '\\')
                    sResult = sResult.Substring(0, sResult.Length - 1);
            }
            int nRet = sResult.LastIndexOf("\\");
            if (nRet != -1)
                sResult = sResult.Substring(nRet + 1);

            return sResult;
        }

        public static int GetObject0(LoginInfo loginInfo,
            Controller mvcControl,
            string libId,
            string weixinId,
            string uri, out string strError)
        {
            strError = "";
            int nRet = 0;

            // 先取出metadata
            string metadata = "";
            string timestamp = "";
            string outputpath = "";
            using (MemoryStream s = new MemoryStream())
            {
                nRet = dp2WeiXinService.Instance.GetObjectMetadata(loginInfo,
                    libId,
                    weixinId,
                    uri,
                    "metadata",
                    s,
                    out metadata,
                    out timestamp,
                    out outputpath,
                    out strError);
                if (nRet == -1)
                    return -1;
            }

            // <file mimetype="application/pdf" localpath="D:\工作清单.pdf" size="188437"
            // lastmodified="2016/9/6 12:45:14" readCount="17" />
            XmlDocument dom = new XmlDocument();
            try
            {
                dom.LoadXml(metadata);
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                return -1;
            }

            // 检查是否有更新，没更新直接用浏览器缓存数据
            string strLastModifyTime = DomUtil.GetAttr(dom.DocumentElement, "lastmodified");//lastmodifytime");
            if (String.IsNullOrEmpty(strLastModifyTime) == false)
            {
                DateTime lastmodified = DateTime.Parse(strLastModifyTime).ToUniversalTime();

                string strIfHeader = mvcControl.Request.Headers["If-Modified-Since"];
                if (String.IsNullOrEmpty(strIfHeader) == false)
                {
                    DateTime isModifiedSince = DateTimeUtil.FromRfc1123DateTimeString(strIfHeader); // .ToLocalTime();

                    if (DateTimeUtil.CompareHeaderTime(isModifiedSince, lastmodified) != 0)
                    {
                        // 修改过
                    }
                    else
                    {
                        // 没有修改过
                        mvcControl.Response.StatusCode = 304;
                        mvcControl.Response.SuppressContent = true;
                        return 0;
                    }
                }

                mvcControl.Response.AddHeader("Last-Modified", DateTimeUtil.Rfc1123DateTimeString(lastmodified)); // .ToUniversalTime()
            }

            // 设置媒体类型
            string strMime = DomUtil.GetAttr(dom.DocumentElement, "mimetype");
            bool bSaveAs = false;
            if (string.IsNullOrEmpty(strMime) == true
                || MatchMIME(strMime, "text") == true
                || MatchMIME(strMime, "image") == true)
            {

            }
            else
            {
                bSaveAs = true;
            }
            if (strMime == "text/plain")
                strMime = "text";
            mvcControl.Response.ContentType = strMime;

            // 是否出现另存为对话框
            if (bSaveAs == true)
            {
                string strClientPath = DomUtil.GetAttr(dom.DocumentElement, "localpath");
                if (strClientPath != "")
                    strClientPath = PureName(strClientPath);

                string strEncodedFileName = HttpUtility.UrlEncode(strClientPath, Encoding.UTF8);
                mvcControl.Response.AddHeader("content-disposition", "attachment; filename=" + strEncodedFileName);
            }

            //设置尺寸
            string strSize = DomUtil.GetAttr(dom.DocumentElement, "size");
            if (String.IsNullOrEmpty(strSize) == false)
            {
                mvcControl.Response.AddHeader("Content-Length", strSize);
            }

            // 关闭buffer
            mvcControl.Response.BufferOutput = false; // 2016/8/31


            // 输出数据流
            nRet = dp2WeiXinService.Instance.GetObjectMetadata(loginInfo,
                libId,
                weixinId,
                uri,
                "metadata,timestamp,data,outputpath",
                mvcControl.Response.OutputStream, //ms,//
                out metadata,
                out timestamp,
                out outputpath,
                out strError);
            if (nRet == -1)
                return -1;

            return 0;
        }



        #endregion


        #region 创建weixin_xxx账户

        private string ManagerUserName = "";
        private string ManagerPassword = "";


        // 用 supervisor 用户名进行登录
        void channels_LoginSupervisor(object sender, LoginEventArgs e)
        {
            MessageConnection connection = sender as MessageConnection;

            if (string.IsNullOrEmpty(this.ManagerUserName) == true)
                throw new Exception("尚未指定超级用户名，无法进行登录");

            e.UserName = this.ManagerUserName;
            e.Password = this.ManagerPassword;
            e.Parameters = "propertyList=biblio_search,libraryUID=weixin" + ",notes=" + HttpUtility.UrlEncode(connection.Name); ;
            return;
        }

        // 创建mserver账号
        public bool CreateMserverUser(string username,
            string password,
            string strDepartment,
            string supervisorUsername,
            string supervisorPassword,
            out string strError)
        {
            strError = "";

            this.ManagerUserName = supervisorUsername;
            this.ManagerPassword = supervisorPassword;

            MessageConnectionCollection channels = null;
            try
            {
                channels = new MessageConnectionCollection();
                channels.Login += channels_LoginSupervisor;

                MessageConnection connection = channels.GetConnectionTaskAsync(
                    _dp2MServerUrl,
                    "supervisor-weixin").Result;

                CancellationToken cancel_token = new CancellationToken();
                string id = Guid.NewGuid().ToString();

                List<User> users = new List<User>();

                User user = new User();
                user.userName = username;  // 这个名称必须是weixin_***
                user.password = password;
                user.rights = "getPatronInfo,searchBiblio,searchPatron,bindPatron,getBiblioInfo,getBiblioSummary,getItemInfo,circulation,getUserInfo,getRes,getSystemParameter,setReaderInfo";
                user.duty = "";
                user.groups = new string[] { "gn:_lib_bb", "gn:_lib_book", "gn:_lib_homePage" };
                user.department = strDepartment;
                user.binding = "ip:[current]";   //20161024 jane 绑定本机ip
                users.Add(user);

                // todo
                MessageResult result = connection.SetUsersTaskAsync("create",
                    users,
                    new TimeSpan(0, 1, 0),
                    cancel_token).Result;

                if (result.Value == -1)
                {
                    strError = "创建用户 '" + username + "' 时出错: " + result.ErrorInfo;
                    goto ERROR1;
                }

                return true;
            }
            catch (MessageException ex)
            {
                strError = this.GetFriendlyException(ex);
                goto ERROR1;
            }
            catch (AggregateException ex)
            {
                if (ex.InnerExceptions.Count > 0 && ex.InnerExceptions[0] is MessageException)
                {
                    MessageException ex1 = (MessageException)ex.InnerExceptions[0];
                    if (ex1.ErrorCode == "Unauthorized")
                    {
                        strError = "dp2mserver超级管理员账户或密码不正确。";
                        goto ERROR1;
                    }
                }

                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }
            finally
            {
                if (channels != null)
                    channels.Login -= channels_LoginSupervisor;

                this.ManagerUserName = "";
                this.ManagerPassword = "";
            }
        ERROR1:

            return false;
        }

        // 测试账号与密码
        private string DetectUserName = "";
        private string DetectPassword = "";

        void channels_LoginUser(object sender, LoginEventArgs e)
        {
            MessageConnection connection = sender as MessageConnection;

            if (string.IsNullOrEmpty(this.DetectUserName) == true)
                throw new Exception("尚未指定用户名，无法进行登录"); ;

            e.UserName = this.DetectUserName;
            e.Password = this.DetectPassword;
            e.Parameters = "propertyList=biblio_search,libraryUID=weixin" + ",notes=" + HttpUtility.UrlEncode(connection.Name);
            return;
        }

        public bool DetectMserverUser(string username, string password, out string strError)
        {
            strError = "";

            this.DetectUserName = username;
            this.DetectPassword = password;

            MessageConnectionCollection channels = null;
            try
            {
                channels = new MessageConnectionCollection();
                channels.Login += channels_LoginUser;

                MessageConnection connection = channels.GetConnectionTaskAsync(
        this._dp2MServerUrl,
        "").Result;
                CancellationToken cancel_token = new CancellationToken();

                string id = Guid.NewGuid().ToString();
                GetMessageRequest request = new GetMessageRequest(id,
                    "",
                    "gn:<default>", // "" 表示默认群组
                    "",
                    "",
                    "",
                    "",
                    "",
                    0,
                    1);

                GetMessageResult result = connection.GetMessage(request,
                   new TimeSpan(0, 1, 0),
                   cancel_token);
                if (result.Value == -1)
                {
                    strError = "检测用户时出错: " + result.ErrorInfo;
                    goto ERROR1;
                }

                return true;
            }
            catch (MessageException ex)
            {
                strError = this.GetFriendlyException(ex);
                goto ERROR1;
            }
            catch (AggregateException ex)
            {
                if (ex.InnerExceptions.Count > 0 && ex.InnerExceptions[0] is MessageException)
                {
                    MessageException ex1 = (MessageException)ex.InnerExceptions[0];
                    if (ex1.ErrorCode == "Unauthorized")
                    {
                        strError = "用户名或密码不存在";
                        goto ERROR1;
                    }
                }

                strError = MessageConnection.GetExceptionText(ex);
                goto ERROR1;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                goto ERROR1;
            }
            finally
            {
                if (channels != null)
                    channels.Login -= channels_LoginUser;

                // 把测试账户置空
                this.DetectUserName = "";
                this.DetectPassword = "";
            }

        ERROR1:
            return false;
        }

        private string GetFriendlyException(MessageException ex)
        {
            string strError = "";
            if (ex.ErrorCode == "Unauthorized")
            {
                strError = "以用户名 '" + ex.UserName + "' 登录时, 用户名或密码不正确";
            }
            else if (ex.ErrorCode == "HttpRequestException")
            {
                strError = "dp2MServer URL 不正确，或 dp2MServer 尚未启动";
            }
            else
            {
                strError = ex.Message;
            }
            return strError;
        }

        #endregion

        public static string GetNeedRight(string group)
        {
            string needRight = "";
            if (group == dp2WeiXinService.C_Group_Bb)
                needRight = dp2WeiXinService.C_Right_SetBb;
            else if (group == dp2WeiXinService.C_Group_Book)
                needRight = dp2WeiXinService.C_Right_SetBook;
            else if (group == dp2WeiXinService.C_Group_HomePage)
                needRight = dp2WeiXinService.C_Right_SetHomePage;

            return needRight;
        }

        public LibEntity GetLibById(string libId)
        {
            Library lib = this.LibManager.GetLibrary(libId);
            if (lib != null)
                return lib.Entity;

            return null;
        }


        /// <summary>
        /// 增量一个条码尾号
        /// </summary>
        /// <param name="barcodeTail"></param>
        /// <returns></returns>
        public static string IncrementBarcode(ref string barcodeTail)
        {

            int nSplitInde = 0;
            for (int i = barcodeTail.Length - 1; i >= 0; i--)
            {
                char s = barcodeTail[i];
                if (s > '9' || s < '0')
                {
                    nSplitInde = i + 1;
                    break;
                }
            }

            string left = barcodeTail.Substring(0, nSplitInde);
            string right = barcodeTail.Substring(nSplitInde);
            if (right != "")
            {
                try
                {
                    int nRight = Convert.ToInt32(right) + 1;
                    right = nRight.ToString().PadLeft(right.Length, '0');
                }
                catch
                { }
            }

            barcodeTail = left + right;


            return barcodeTail;
        }


    }
}
