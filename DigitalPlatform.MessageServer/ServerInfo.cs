﻿using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace DigitalPlatform.MessageServer
{
    public static class ServerInfo
    {
        // 检索请求管理集合
        public static SearchTable SearchTable = new SearchTable(true);

        // 通道集合
        public static ConnectionTable ConnectionTable = new ConnectionTable();

        // 配置文件 XmlDocument
        public static XmlDocument ConfigDom = new XmlDocument();

        // 用户数据库
        public static UserDatabase UserDatabase = new UserDatabase();

        // 消息数据库
        public static MessageDatabase MessageDatabase = new MessageDatabase();

        // 组数据库
        public static GroupDatabase GroupDatabase = new GroupDatabase();

        static MongoClient _mongoClient = null;

        public static void Initial(string strDataDir)
        {
            if (Directory.Exists(strDataDir) == false)
                throw new Exception("数据目录 '" + strDataDir + "' 尚未创建");

            string strCfgFileName = Path.Combine(strDataDir, "config.xml");
            ConfigDom.Load(strCfgFileName);

            // 元素 <mongoDB>
            // 属性 connectionString / instancePrefix
            XmlElement node = ConfigDom.DocumentElement.SelectSingleNode("mongoDB") as XmlElement;
            if (node != null)
            {
                string strMongoDbConnStr = node.GetAttribute("connectionString");
                string strMongoDbInstancePrefix = node.GetAttribute("instancePrefix");

                _mongoClient = new MongoClient(strMongoDbConnStr);

                UserDatabase.Open(_mongoClient, strMongoDbInstancePrefix, "user");
                MessageDatabase.Open(_mongoClient, strMongoDbInstancePrefix, "message");
                GroupDatabase.Open(_mongoClient, strMongoDbInstancePrefix, "group");
            }
            else
                throw new Exception("config.xml 中尚未配置 mongoDB 元素");

        }

        // 准备退出
        public static void Exit()
        {
            SearchTable.Exit();
            SearchTable.Dispose();
        }
    }

}
