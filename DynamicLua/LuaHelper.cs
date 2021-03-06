﻿/* Copyright 2014 Niklas Rother
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NLua;
using System.IO;
using System.Reflection;

namespace DynamicLua
{
    static class LuaHelper
    {
        public const int RandomNameLength = 8;

        public static Random Random { get; private set; }

        static LuaHelper() //static ctor
        {
            Random = new Random();
        }


        /// <summary>
        /// Returns a random string with the specified lenght. Use the overload
        /// without paramters for the default length.
        /// This can be safely used as a Lua Variable Name, but is not checked
        /// for collsions.
        /// </summary>
        /// <remarks>see http://dotnet-snippets.de/dns/passwort-generieren-SID147.aspx</remarks>
        public static string GetRandomString(int lenght)
        {
            string ret = string.Empty;
            StringBuilder SB = new System.Text.StringBuilder();
            string Content = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            for (int i = 0; i < lenght; i++)
                SB.Append(Content[Random.Next(Content.Length)]);
            return SB.ToString();
        }

        /// <summary>
        /// Returns a random string with lenght LuaHelper.RandomNameLength.
        /// This can be safely used as a Lua Variable Name, but is not checked
        /// for collsions.
        /// </summary>
        public static string GetRandomString()
        {
            return GetRandomString(RandomNameLength);
        }

        /// <summary>
        /// Unwaps an object comming from LuaInterface for use in DynamicLua.
        /// </summary>
        public static object UnWrapObject(object wrapped, Lua state, string name = null)
        {
            if (wrapped is LuaTable)
                return new DynamicLuaTable((LuaTable)wrapped, state, name);
            else if (wrapped is LuaFunction)
                return new DynamicLuaFunction((LuaFunction)wrapped, state);
            else if (wrapped is MulticastDelegate)
                return new DynamicLuaFunction(state.GetFunction(name), state);
            else
                return wrapped;
        }

        /// <summary>
        /// Wraps an object to prepare it for passing to LuaInterface. If no name is specified, a 
        /// random one with the default length is used.
        /// </summary>
        public static object WrapObject(object toWrap, Lua state, string name = null)
        {
            if (toWrap is DynamicArray)
            {
                //Someone passed an DynamicArray diretly back to Lua.
                //He wanted to pass the value in the array, so we extract it.
                //When there is more than one value, this method will ignore these extra value.
                //This could happen in a situation like this: lua.tmp = lua("return a,b");, but
                //that doesn't make sense.
                toWrap = (toWrap as dynamic)[0];
            }

            if (toWrap is MulticastDelegate)
            {
                //We have to deal with a problem here: RegisterFunction does not really create
                //a new function, but a userdata with a __call metamethod. This works fine in all
                //except two cases: When Lua looks for an __index or __newindex metafunction and finds
                //a table or userdata, Lua tries to redirect the read/write operation to that table/userdata.
                //In case of our function that is in reality a userdata this fails. So we have to check
                //for these function and create a very thin wrapper arround this to make Lua see a function instead
                //the real userdata. This is no problem for the other metamethods, these are called independent
                //from their type. (If they are not nil ;))
                MulticastDelegate function = (toWrap as MulticastDelegate);

                if (name != null && (name.EndsWith("__index") || name.EndsWith("__newindex")))
                {
                    string tmpName = LuaHelper.GetRandomString();
                    state.RegisterFunction(tmpName, function.Target, function.Method);
                    state.DoString(String.Format("function {0}(...) return {1}(...) end", name, tmpName), "DynamicLua internal operation");
                }
                else
                {
                    if (name == null)
                        name = LuaHelper.GetRandomString();
                    state.RegisterFunction(name, function.Target, function.Method);
                }
                return null;
            }
            else if (toWrap is DynamicLuaTable)
            {
                dynamic dlt = toWrap as DynamicLuaTable;
                return (LuaTable)dlt;
            }
            else
                return toWrap;
        }

        /// <summary>
        /// Path where the native DLLs are copied to.
        /// </summary>
        private static string NativeDllPath
        {
            get
            {
                if (_nativeDllPath == null)
                    _nativeDllPath = Path.Combine(Path.GetTempPath(), "DynamicLuaNativeDLLs", Assembly.GetExecutingAssembly().GetName().Version.ToString(), Environment.Is64BitProcess ? "x64" : "x86");
                return _nativeDllPath;
            }
        }
        private static string _nativeDllPath;

        /// <summary>
        /// Extract the native DLL to the <see cref="NativeDllPath"/>
        /// </summary>
        /// <param name="dllName"></param>
        private static void ExtractNativeDll(string dllName)
        {
            string extractFullName = Path.Combine(NativeDllPath, dllName);

            if (File.Exists(extractFullName))
                return; //file exists, no need to unpack it again

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(string.Format("{0}.{1}.{2}", "DynamicLua", (Environment.Is64BitProcess ? "x64" : "x86"), dllName)))
            {
                try
                {
                    Directory.CreateDirectory(NativeDllPath);
                    using (var fs = File.Create(extractFullName))
                        stream.CopyTo(fs);
                }
                catch (Exception)
                {
                    Console.WriteLine("Error while unpacking the native DLL, ignoring");
                    //ignore, probably a race condition ("file does not exists, but we cannot write to it")
                }
            }
        }

        /// <summary>
        /// This method extract the included native Lua-DLLs.
        /// It's important to extract the correct 32/64-bit version.
        /// We extract to %tmp%, we might not have write access to the
        /// current assembliy's locations.
        /// We append the path to the extracted DLLs to %PATH%, to make
        /// allow the system to find the DLL.
        /// </summary>
        internal static void ExtractNativeDlls()
        {
            ExtractNativeDll("lua52.dll");
            ExtractNativeDll("msvcr110.dll");
            Environment.SetEnvironmentVariable("PATH", string.Concat(NativeDllPath, ";", Environment.GetEnvironmentVariable("PATH")));
        }
    }
}
