﻿/*

    ==========================================================================================

    Copyright (C) 2018 Jamie Goodson (aka MisterGoodson) (goodsonjamie@yahoo.co.uk)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.

    ==========================================================================================

 * Sends supplied JSON string to Capture Manager.
 * JSON string can (and should) be supplied in multiple separate calls to this extension
 * (as to avoid the Arma buffer limit).
 */

using RGiesecke.DllExport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using Newtonsoft.Json;

namespace OCAPExporter
{
    public class Main
    {
        static readonly string DATETIME_START = DateTime.Now.ToString("yyyy-dd-M_HH-mm-ss");
        const string LOGDIR = "ocap_logs";
        static readonly string LOGFILE = Path.Combine(LOGDIR, String.Format("log_{0}.txt", DATETIME_START));
        static readonly string LOGJSONFILE = Path.Combine(LOGDIR, String.Format("publish_{0}.json", DATETIME_START));
        static DirectoryInfo logDirInfo  = Directory.CreateDirectory(LOGDIR);
        static Dictionary<string, object> captureData;
        static List<object> entities;
        static List<object> events;

        // Commands
        const string CMD_INIT = "init";
        const string CMD_NEW_UNIT = "new_unit";
        const string CMD_NEW_VEHICLE = "new_vehicle";
        const string CMD_UPDATE_UNIT = "update_unit";
        const string CMD_UPDATE_VEHICLE = "update_vehicle";
        const string CMD_PUBLISH = "publish";
        const string CMD_LOG = "log";

        [DllExport("RVExtension", CallingConvention = System.Runtime.InteropServices.CallingConvention.Winapi)]
        public static void RvExtension(StringBuilder output, int outputSize, string function)
        {
            outputSize--;
            output.Append("Please provide args.");
        }

        [DllExport("RVExtensionArgs", CallingConvention = System.Runtime.InteropServices.CallingConvention.Winapi)]
        public static int RvExtensionArgs(
            StringBuilder output,
            int outputSize,
            [MarshalAs(UnmanagedType.LPStr)] string function,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr, SizeParamIndex = 4)] string[] args,
            int argsCnt)
        {
            args = cleanArgs(args);

            switch (function) {
                case CMD_INIT:
                    Init();
                    break;
                case CMD_UPDATE_UNIT:
                    UpdateUnit(args);
                    break;
                case CMD_UPDATE_VEHICLE:
                    UpdateVehicle(args);
                    break;
                case CMD_NEW_UNIT:
                    NewUnit(args);
                    break;
                case CMD_NEW_VEHICLE:
                    NewVehicle(args);
                    break;
                case CMD_PUBLISH:
                    Publish(args);
                    break;
                case CMD_LOG:
                    Log(args[0], fromGame: true);
                    break;
            }

            // Send output to Arma
            outputSize--;
            output.Append("Call successful");

            return 200;
        }

        public static void Init()
        {
            entities = new List<object>();
            events = new List<object>();
            captureData = new Dictionary<string, object>() {
                {"captureId", "" },
                {"worldName", "" },
                {"missionName", "" },
                {"author", "" },
                {"captureDelay", 0 },
                {"frameCount", 0 },
                { "entities", entities },
                { "events", events },
           };

            Log("Initialised new capture");
        }

        public static void NewUnit(string[] unitData)
        {
            if (!areArgsValid("NewUnit", unitData, 6)) { return; };
            entities.Add(
                new List<object> {
                    new List<object> { // Header
                        int.Parse(unitData[0]), // Start frame number
                        1, // Is unit
                        int.Parse(unitData[1]), // Id
                        unitData[2], // Name
                        unitData[3], // Group
                        unitData[4], // Side
                        int.Parse(unitData[5]), // Is player
                    },
                    new List<object> { }, // States
                    new List<object> { }, // Frames fired
                }
            );
        }

        public static void NewVehicle(string[] vehicleData)
        {
            if (!areArgsValid("NewVehicle", vehicleData, 4)) { return; };
            entities.Add(
                new List<object> {
                    new List<object> { // Header
                        vehicleData[0], // Start frame number
                        0, // Is unit
                        int.Parse(vehicleData[1]), // Id
                        vehicleData[2], // Name
                        vehicleData[3], // Class
                    },
                    new List<object> { }, // States
                }
            );
        }

        public static void UpdateUnit(string[] unitData)
        {
            if (!areArgsValid("UpdateUnit", unitData, 5)) { return; };
            int id = int.Parse(unitData[0]);
            List<object> unit = (List<object>) entities[id];
            List<object> states = (List<object>) unit[1];

            states.Add(new List<object>
            {
                StringToIntArray(unitData[1]), // Position
                int.Parse(unitData[2]), // Direction
                int.Parse(unitData[3]), // Is alive
                int.Parse(unitData[4]), // Is in vehicle
            });
        }

        public static void UpdateVehicle(string[] vehicleData)
        {
            if (!areArgsValid("UpdateVehicle", vehicleData, 5)) { return; };
            int id = int.Parse(vehicleData[0]);
            List<object> vehicle = (List<object>) entities[id];
            List<object> states = (List<object>) vehicle[1];

            states.Add(new List<object>
            {
                StringToIntArray(vehicleData[1]), // Position
                int.Parse(vehicleData[2]), // Direction
                int.Parse(vehicleData[3]), // Is alive
                StringToIntArray(vehicleData[4]), // Crew ids
            });
        }

        // TODO: Run this in separate thread
        public static void Publish(string[] args)
        {
            if (!areArgsValid("Publish", args, 6)) { return; };
            Log("Publishing data...");
            Log("Publish args:");
            Log(String.Join(",", args));
            string json = null;
            string postUrl = FormatUrl(args[0]) + "/import";
            string missionName = args[2];
            string captureId = String.Format("{0}_{1}", missionName, DateTime.Now.ToString("yyyy-dd-M_HH-mm-ss"));

            captureData["captureId"] = captureId;
            captureData["worldName"] = args[1];
            captureData["missionName"] = missionName;
            captureData["author"] = args[3];
            captureData["captureDelay"] = int.Parse(args[4]);
            captureData["frameCount"] = int.Parse(args[5]);

            try
            {
                json = JsonConvert.SerializeObject(captureData);
            }
            catch (Exception e)
            {
                Log("Could not serialise data into json", isError: true);
                Log(e.ToString(), isError: true);
            }

            if (json == null) { return; };
            LogJson(json);

            // POST capture data to OCAP webserver
            try
            {
                Log("Sending POST request to " + postUrl);
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(10);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var result = http.PostAsync(postUrl, content).Result;
                    string resultContent = result.Content.ReadAsStringAsync().Result;
                    Log("Web server responded with: " + resultContent);
                }
            }
            catch (Exception e)
            {
                Log("An error occurred while sending POST request. Possibly due to timeout.", isError: true);
                Log(e.ToString(), isError: true);
            }

            Log("Publish complete");
        }

        public static int[] StringToIntArray(string input)
        {
            input = input.Replace("[", "").Replace("]", "");
            if (input.Length == 0) { return new int[0]; };

            string[] inputArray = input.Split(',');
            try
            {
                return Array.ConvertAll(inputArray, int.Parse);
            } catch (Exception e)
            {
                Log("Could not convert string to int array: " + input, isError: true);
                Log(e.ToString(), isError: true);
            }

            return null;
        }

        public static string[] cleanArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                args[i] = arg.TrimStart('\"').TrimEnd('\"');
            };

            return args;
        }

        // Check if length of args in args array matches expected length
        public static bool areArgsValid(string funcName, string[] args, int expectedLength)
        {
            int length = args.Length;
            if (length != expectedLength)
            {
                Log(String.Format("{0}: {1} args provided, {2} expected.", funcName, length, expectedLength), isError: true);
                Log(String.Format("Args provided: {0}", args), isError: true);
                return false;
            }

            return true;
        }

        // Format a URL if malformed
        public static string FormatUrl(string url, bool removeTrailingSlash = true)
        {
            // Fallback to http if protocol not defined
            if (!(url.StartsWith("http://") || url.StartsWith("https://")))
            {
                url = "http://" + url;
            }

            if (url.EndsWith("/") && removeTrailingSlash)
            {
                url = url.TrimEnd('/');
            }

            return url;
        }

        // Write string to log file and console.
        public static void Log(string str = "", bool isError = false, bool fromGame = false)
        {
            if (isError)
            {
                str = "Error: " + str;
            }

            if (fromGame)
            {
                str = "Arma | " + str;
            } else
            {
                str = "Ext  | " + str;
            }

            Console.WriteLine(str);
            File.AppendAllText(LOGFILE, String.Format("{0} | {1}{2}", DateTime.Now.ToString("HH:mm:ss"), str, Environment.NewLine));
        }

        public static void LogJson(string json)
        {
            File.WriteAllText(LOGJSONFILE, json);
        }
    }
}
