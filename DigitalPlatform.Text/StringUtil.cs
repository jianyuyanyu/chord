﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DigitalPlatform.Text
{
    /// <summary>
    /// 字符串实用函数
    /// </summary>
    public static class StringUtil
    {
        public static long TryGetSubInt64(string strText,
    char seperator,
    int index,
    long default_value = 0)
        {
            try
            {
                return GetSubInt64(strText, seperator, index, default_value);
            }
            catch
            {
                return default_value;
            }
        }

        // exception:
        //      抛出 Int64.Parse() 要抛出的那些异常
        public static long GetSubInt64(string strText, 
            char seperator, 
            int index, 
            long default_value = 0)
        {
            string str_value = GetSubString(strText, seperator, index);
            if (string.IsNullOrEmpty(str_value) == true)
                return default_value;

            return Int64.Parse(str_value);
        }

        public static string GetSubString(string strText, char seperator, int index)
        {
            string[] parts = strText.Split(new char[] { seperator });
            if (index >= parts.Length)
                return null;
            return parts[index];
        }

        public static List<string> GetStringList(string strText,
            char delimeter)
        {
            if (string.IsNullOrEmpty(strText) == true)
                return new List<string>();

            string[] parts = strText.Split(new char[] { delimeter });
            List<string> results = new List<string>();
            results.AddRange(parts);
            return results;
        }

        /// <summary>
        /// 检测一个列表字符串是否包含一个具体的值
        /// </summary>
        /// <param name="strList">列表字符串。用逗号分隔多个子串</param>
        /// <param name="strOne">要检测的一个具体的值</param>
        /// <returns>false 没有包含; true 包含</returns>
        public static bool Contains(string strList, string strOne, char delimeter = ',')
        {
            if (string.IsNullOrEmpty(strList) == true)
                return false;
            string[] list = strList.Split(new char[] { delimeter }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string s in list)
            {
                if (strOne == s)
                    return true;
            }

            return false;
        }

        // parameters:
        //      strPrefix 前缀。例如 "getreaderinfo:"
        // return:
        //      null    没有找到前缀
        //      ""      找到了前缀，并且值部分为空
        //      其他     返回值部分
        public static string GetParameterByPrefix(string strList, string strPrefix)
        {
            if (string.IsNullOrEmpty(strList) == true)
                return "";
            string[] list = strList.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string s in list)
            {
                if (s.StartsWith(strPrefix) == true)
                    return s.Substring(strPrefix.Length);
            }

            return null;
        }

        //===================
        // 任延华 2015-12-22 加

        public static string MakePathList(List<string> aPath,
            string strSep)
        {
            // 2012/9/7
            if (aPath.Count == 0)
                return "";

            string[] pathlist = new string[aPath.Count];
            aPath.CopyTo(pathlist);

            return String.Join(strSep, pathlist);
        }

        // 得到用16进制表示的时间戳字符串
        public static string GetHexTimeStampString(byte[] baTimeStamp)
        {
            if (baTimeStamp == null)
                return "";
            string strText = "";
            for (int i = 0; i < baTimeStamp.Length; i++)
            {
                string strHex = Convert.ToString(baTimeStamp[i], 16);
                strText += strHex.PadLeft(2, '0');
            }

            return strText;
        }

        // 得到byte[]类型的时间戳
        public static byte[] GetTimeStampByteArray(string strHexTimeStamp)
        {
            if (strHexTimeStamp == "")
                return null;

            byte[] result = new byte[strHexTimeStamp.Length / 2];

            for (int i = 0; i < strHexTimeStamp.Length / 2; i++)
            {
                string strHex = strHexTimeStamp.Substring(i * 2, 2);
                result[i] = Convert.ToByte(strHex, 16);

            }

            return result;
        }

        public static List<string> SplitList(string strText)
        {
            // 2011/12/26
            if (string.IsNullOrEmpty(strText) == true)
                return new List<string>();

            string[] parts = strText.Split(new char[] { ',' });
            List<string> results = new List<string>();
            results.AddRange(parts);
            return results;
        }

        // 检测一个字符串的头部
        public static bool HasHead(string strText,
            string strHead,
            bool bIgnoreCase = false)
        {
            // 2013/9/11
            if (strText == null)
                strText = "";
            if (strHead == null)
                strHead = "";

            if (strText.Length < strHead.Length)
                return false;

            string strPart = strText.Substring(0, strHead.Length);  // BUG!!! strText.Substring(strHead.Length);

            // 2015/4/3
            if (bIgnoreCase == true)
            {
                if (string.Compare(strPart, strHead, true) == 0)
                    return true;
                return false;
            }

            if (strPart == strHead)
                return true;

            return false;
        }

        // 构造路径列表字符串，逗号分隔
        public static string MakePathList(List<string> aPath)
        {
            // 2012/9/7
            if (aPath.Count == 0)
                return "";

            string[] pathlist = new string[aPath.Count];
            aPath.CopyTo(pathlist);

            return String.Join(",", pathlist);
        }

        // 修改字符串某一个位字符
        public static string SetAt(string strText, int index, char c)
        {
            strText = strText.Remove(index, 1);
            strText = strText.Insert(index, new string(c, 1));

            return strText;
        }

        // 获取引导的{...}内容。注意返回值不包括花括号
        public static string GetLeadingCommand(string strLine)
        {
            if (string.IsNullOrEmpty(strLine) == true)
                return null;

            // 关注"{...}"
            if (strLine[0] == '{')
            {
                int nRet = strLine.IndexOf("}");
                if (nRet != -1)
                    return strLine.Substring(1, nRet - 1).Trim();
            }

            return null;
        }

        // 检测字符串是否为纯数字(前面可以包含一个'-'号)
        public static bool IsNumber(string s)
        {
            if (string.IsNullOrEmpty(s) == true)
                return false;

            bool bFoundNumber = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '-' && bFoundNumber == false)
                {
                    continue;
                }
                if (s[i] > '9' || s[i] < '0')
                    return false;
                bFoundNumber = true;
            }
            return true;
        }

        //比较字符串是否符合正则表达式
        public static bool RegexCompare(string strPattern,
            RegexOptions regOptions,
            string strInstance)
        {
            Regex r = new Regex(strPattern, regOptions);
            System.Text.RegularExpressions.Match m = r.Match(strInstance);

            if (m.Success)
                return true;
            else
                return false;
        }

        //===================


    }
}
