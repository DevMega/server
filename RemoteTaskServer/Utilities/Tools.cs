﻿#region

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Web;
using NetFwTypeLib;
using RemoteTaskServer.WebServer;
using UlteriusServer.TaskServer.Services.Network;
using static System.Security.Principal.WindowsIdentity;

#endregion

namespace UlteriusServer.Utilities
{
    internal class Tools
    {
        private const string INetFwPolicy2ProgID = "HNetCfg.FwPolicy2";
        private const string INetFwRuleProgID = "HNetCfg.FWRule";

        public static bool HasInternetConnection
        {
            // There is no way you can reliably check if there is an internet connection, but we can come close
            get
            {
                var result = false;

                try
                {
                    if (NetworkInterface.GetIsNetworkAvailable())
                    {
                        using (var p = new Ping())
                        {
                            var pingReply = p.Send("8.8.8.8", 15000);
                            if (pingReply != null)
                                result =
                                    (pingReply.Status == IPStatus.Success) ||
                                    (p.Send("8.8.4.4", 15000)?.Status == IPStatus.Success) ||
                                    (p.Send("4.2.2.1", 15000)?.Status == IPStatus.Success);
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                return result;
            }
        }

        public static void ShowNetworkTraffic()
        {
            var performanceCounterCategory = new PerformanceCounterCategory("Network Interface");
            var instance = performanceCounterCategory.GetInstanceNames()[0]; // 1st NIC !
            var performanceCounterSent = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance);
            var performanceCounterReceived = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance);

            for (var i = 0; i < 10; i++)
            {
                Console.WriteLine("bytes sent: {0}k\tbytes received: {1}k", performanceCounterSent.NextValue()/1024,
                    performanceCounterReceived.NextValue()/1024);
                Thread.Sleep(500);
            }
        }


        private void ClosePort(string name)
        {
            var firewallPolicy = getComObject<INetFwPolicy2>(INetFwPolicy2ProgID);
            firewallPolicy.Rules.Remove(name);
        }

        private static T getComObject<T>(string progID)
        {
            var t = Type.GetTypeFromProgID(progID, true);
            return (T) Activator.CreateInstance(t);
        }

        private static void OpenPort(ushort port, string name)
        {
            var firewallPolicy = getComObject<INetFwPolicy2>(INetFwPolicy2ProgID);
            var firewallRule = getComObject<INetFwRule2>(INetFwRuleProgID);
            var existingRule = firewallPolicy.Rules.OfType<INetFwRule>().FirstOrDefault(x => x.Name == name);
            if (existingRule == null)
            {
                firewallRule.Description = name;
                firewallRule.Name = name;
                firewallRule.Action = NET_FW_ACTION_.NET_FW_ACTION_ALLOW;
                firewallRule.Enabled = true;
                firewallRule.InterfaceTypes = "All";
                firewallRule.Protocol = (int) NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_TCP;
                firewallRule.LocalPorts = port.ToString();
                firewallPolicy.Rules.Add(firewallRule);
            }
        }

  
        public static void GenerateSettings()
        {
            //web server settings
            Settings.Get()["General"] = new Settings.Header
            {
                {
                    "Version", Assembly.GetExecutingAssembly().GetName().Version
                },
                {
                    "UploadLogs", false
                },
                {
                    "Github", "https://github.com/Ulterius"
                },
                {
                    "ServerIssues", "https://github.com/Ulterius/server/issues"
                },
                {
                    "ClientIssues", "https://github.com/Ulterius/client/issues"
                },
                {
                    //this is kind of nasty 
                    "Maintainers", new[]
                    {
                        new
                        {
                            Name = "Andrew Sampson",
                            Twitter = "https://twitter.com/Andrewmd5",
                            Github = "https://github.com/codeusa",
                            Website = "https://andrew.im/"
                        },
                        new
                        {
                            Name = "Evan Banyash",
                            Twitter = "https://twitter.com/frobthebuilder",
                            Github = "https://github.com/FrobtheBuilder",
                            Website = "http://banyash.com/"
                        }
                    }
                }
            };
            Settings.Get()["WebServer"] = new Settings.Header
            {
                {
                    "WebFilePath", HttpServer.DefaultPath
                },
                {
                    "WebServerPort", 22006
                },
                {
                    "UseWebServer", true
                }
            };
            Settings.Get()["TaskServer"] = new Settings.Header
            {
                {
                    "TaskServerPort", 22007
                },
                {
                    "Encryption", true
                }
            };
            Settings.Get()["Network"] = new Settings.Header
            {
                {
                    "SkipHostNameResolve", false
                },
                {
                    "BindLocal", false
                }
            };
            Settings.Get()["Plugins"] = new Settings.Header
            {
                {
                    "LoadPlugins", true
                }
            };
            Settings.Get()["ScreenShare"] = new Settings.Header
            {
                {
                    "ScreenSharePass", string.Empty
                },
                {
                    "ScreenSharePort", 22009
                }
            };
            Settings.Get()["Terminal"] = new Settings.Header
            {
                {
                    "AllowTerminal", true
                }
            };

            Settings.Get()["Debug"] = new Settings.Header
            {
                {
                    "TraceDebug", true
                }
            };

            Settings.Save();
        }

        public static void ConfigureServer()
        {
            if (Settings.Empty)
            {
                //setup listen sh
                var prefix = "http://*:22006/";
                var username = Environment.GetEnvironmentVariable("USERNAME");
                var userdomain = Environment.GetEnvironmentVariable("USERDOMAIN");
                var command = $@"/C netsh http add urlacl url={prefix} user={userdomain}\{username} listen=yes";
                Process.Start("CMD.exe", command);
                OpenPort(22006, "Ulterius Web Server");
                OpenPort(22007, "Ulterius Task Server");
                OpenPort(22008, "Ulterius Terminal Server");
                OpenPort(22009, "Ulterius ScreenShare");
                GenerateSettings();
            }
        }


        public static bool IsAdmin()
        {
            return new WindowsPrincipal(GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static string GetQueryString(string url, string key)
        {
            var queryString = string.Empty;

            var uri = new Uri(url);
            var newQueryString = HttpUtility.ParseQueryString(uri.Query);
            if (newQueryString[key] != null)
            {
                queryString = newQueryString[key];
            }


            return queryString;
        }
    }
}