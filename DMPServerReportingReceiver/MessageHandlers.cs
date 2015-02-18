using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using MessageStream;
using MessageStream2;

namespace DMPServerReportingReceiver
{
    public class MessageHandlers
    {
        public static int reportID;

        private static DatabaseConnection databaseConnection
        {
            get
            {
                return MainClass.databaseConnection;
            }
        }

        public static void HandleHeartbeat(ClientObject client, byte[] messageData)
        {
            //Don't care - these only keep the connection alive
        }

        public static void HandleReportingVersion1(ClientObject client, byte[] messageData)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            using (MessageStream.MessageReader mr = new MessageStream.MessageReader(messageData, false))
            {
                string serverHash = mr.Read<string>();
                string serverName = mr.Read<string>();
                string description = mr.Read<string>();
                int gamePort = mr.Read<int>();
                string gameAddress = mr.Read<string>();
                int protocolVersion = mr.Read<int>();
                string programVersion = mr.Read<string>();
                int maxPlayers = mr.Read<int>();
                int modControl = mr.Read<int>();
                string modControlSha = mr.Read<string>();
                int gameMode = mr.Read<int>();
                bool cheats = mr.Read<bool>();
                int warpMode = mr.Read<int>();
                long universeSize = mr.Read<long>();
                string banner = mr.Read<string>();
                string homepage = mr.Read<string>();
                int httpPort = mr.Read<int>();
                string admin = mr.Read<string>();
                string team = mr.Read<string>();
                string location = mr.Read<string>();
                bool fixedIP = mr.Read<bool>();
                string[] players = mr.Read<string[]>();
                //Check if this is a new server
                client.serverHash = serverHash;
                Dictionary<string, object> hashParameters = new Dictionary<string, object>();
                hashParameters["@hash"] = serverHash;

                //Initialize if needed
                if (!client.initialized)
                {
                    client.initialized = true;
                    MainClass.DisconnectOtherClientsWithHash(client, serverHash);
                    string sqlQuery = "CALL gameserverinit(@serverhash, @namex, @descriptionx, @gameportx, @gameaddressx, @protocolx, @programversion, @maxplayersx, @modcontrolx, @modcontrolshax, @gamemodex, @cheatsx, @warpmodex, @universex, @bannerx, @homepagex, @httpportx, @adminx, @teamx, @locationx, @fixedipx);";
                    Dictionary<string, object> parameters = new Dictionary<string, object>();
                    parameters["@serverhash"] = serverHash;
                    parameters["@namex"] = serverName;
                    if (serverName.Length > 255)
                    {
                        serverName = serverName.Substring(0, 255);
                    }
                    parameters["@descriptionx"] = description;
                    parameters["@gameportx"] = gamePort;
                    parameters["@gameaddressx"] = gameAddress;
                    parameters["@protocolx"] = protocolVersion;
                    parameters["@programversion"] = programVersion;
                    parameters["@maxplayersx"] = maxPlayers;
                    parameters["@modcontrolx"] = modControl;
                    parameters["@modcontrolshax"] = modControlSha;
                    parameters["@gamemodex"] = gameMode;
                    parameters["@cheatsx"] = cheats;
                    parameters["@warpmodex"] = warpMode;
                    parameters["@universex"] = universeSize;
                    parameters["@bannerx"] = banner;
                    parameters["@homepagex"] = homepage;
                    parameters["@httpportx"] = httpPort;
                    parameters["@adminx"] = admin;
                    parameters["@teamx"] = team;
                    parameters["@locationx"] = location;
                    parameters["@fixedipx"] = fixedIP;
                    Console.WriteLine("Server " + serverHash + " is online!");
                    try
                    {
                        databaseConnection.ExecuteNonReader(sqlQuery, parameters);
                    }
                    catch
                    {
                        int thisID = Interlocked.Increment(ref reportID);
                        string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                        string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                        string errorBin = Path.Combine(errorPath, thisID + ".bin");
                        File.WriteAllBytes(errorBin, messageData);
                        using (StreamWriter errorFile = new StreamWriter(errorParameters))
                        {
                            errorFile.WriteLine("Reporting version 1");
                            errorFile.WriteLine("State: Init");
                            errorFile.WriteLine("SQL: " + sqlQuery);
                            foreach (KeyValuePair<string,object> kvp in parameters)
                            {
                                errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                            }
                        }
                        throw;
                    }
                }

                if (client.connectedPlayers == null)
                {
                    //Report connected players as connected
                    foreach (string connectedPlayer in players)
                    {
                        Console.WriteLine("Player " + connectedPlayer + " joined " + serverHash);
                        Dictionary<string, object> playerParams = new Dictionary<string, object>();
                        playerParams["@hash"] = serverHash;
                        playerParams["@player"] = connectedPlayer;
                        string sqlQuery = "CALL gameserverplayer(@hash, @player, '1')";
                        try
                        {
                            databaseConnection.ExecuteNonReader(sqlQuery, playerParams);
                        }
                        catch
                        {
                            int thisID = Interlocked.Increment(ref reportID);
                            string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                            string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                            string errorBin = Path.Combine(errorPath, thisID + ".bin");
                            File.WriteAllBytes(errorBin, messageData);
                            using (StreamWriter errorFile = new StreamWriter(errorParameters))
                            {
                                errorFile.WriteLine("Reporting version 1");
                                errorFile.WriteLine("State: Init Players");
                                errorFile.WriteLine("SQL: " + sqlQuery);
                                foreach (KeyValuePair<string,object> kvp in playerParams)
                                {
                                    errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                                }
                            }
                            throw;
                        }
                    }
                }
                else
                {
                    foreach (string player in players)
                    {
                        Console.WriteLine("Player: " + player);
                    }
                    //Take all the currently connected players and remove the players that were connected already to generate a list of players to be added
                    List<string> addList = new List<string>(players);
                    foreach (string player in client.connectedPlayers)
                    {
                        if (addList.Contains(player))
                        {
                            addList.Remove(player);
                        }
                    }
                    //Take all the old players connected and remove the players that are connected already to generate a list of players to be removed
                    List<string> removeList = new List<string>(client.connectedPlayers);
                    foreach (string player in players)
                    {
                        if (removeList.Contains(player))
                        {
                            removeList.Remove(player);
                        }
                    }
                    //Add new players
                    foreach (string player in addList)
                    {
                        Console.WriteLine("Player " + player + " joined " + serverHash);
                        Dictionary<string, object> playerParams = new Dictionary<string, object>();
                        playerParams["hash"] = serverHash;
                        playerParams["player"] = player;
                        string sqlQuery = "CALL gameserverplayer(@hash ,@player, '1')";
                        try
                        {
                            databaseConnection.ExecuteNonReader(sqlQuery, playerParams);
                        }
                        catch
                        {
                            int thisID = Interlocked.Increment(ref reportID);
                            string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                            string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                            string errorBin = Path.Combine(errorPath, thisID + ".bin");
                            File.WriteAllBytes(errorBin, messageData);
                            using (StreamWriter errorFile = new StreamWriter(errorParameters))
                            {
                                errorFile.WriteLine("Reporting version 1");
                                errorFile.WriteLine("State: Add Player");
                                errorFile.WriteLine("SQL: " + sqlQuery);
                                foreach (KeyValuePair<string,object> kvp in playerParams)
                                {
                                    errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                                }
                            }
                            throw;
                        }
                    }
                    //Remove old players
                    foreach (string player in removeList)
                    {
                        Console.WriteLine("Player " + player + " left " + serverHash);
                        Dictionary<string, object> playerParams = new Dictionary<string, object>();
                        playerParams["hash"] = serverHash;
                        playerParams["player"] = player;
                        string sqlQuery = "CALL gameserverplayer(@hash ,@player, '0')";
                        try
                        {
                            databaseConnection.ExecuteNonReader(sqlQuery, playerParams);
                        }
                        catch
                        {
                            int thisID = Interlocked.Increment(ref reportID);
                            string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                            string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                            string errorBin = Path.Combine(errorPath, thisID + ".bin");
                            File.WriteAllBytes(errorBin, messageData);
                            using (StreamWriter errorFile = new StreamWriter(errorParameters))
                            {
                                errorFile.WriteLine("Reporting version 1");
                                errorFile.WriteLine("State: Remove Player");
                                errorFile.WriteLine("SQL: " + sqlQuery);
                                foreach (KeyValuePair<string,object> kvp in playerParams)
                                {
                                    errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                                }
                            }
                            throw;
                        }
                    }
                }
                //Save connected players for tracking
                client.connectedPlayers = players;

                sw.Stop();
                Console.WriteLine("Handled report from " + serverName + " (" + client.address + "), Protocol " + protocolVersion + ", Program Version: " + programVersion + ", Time: " + sw.ElapsedMilliseconds);
            }
        }

        //Exactly the same as above, but with MessageReader2
        public static void HandleReportingVersion2(ClientObject client, byte[] messageData)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            ReportTee.QueueReport(client, messageData);
            using (MessageStream2.MessageReader mr = new MessageStream2.MessageReader(messageData))
            {
                string serverHash = mr.Read<string>();
                string serverName = mr.Read<string>();
                string description = mr.Read<string>();
                int gamePort = mr.Read<int>();
                string gameAddress = mr.Read<string>();
                int protocolVersion = mr.Read<int>();
                string programVersion = mr.Read<string>();
                int maxPlayers = mr.Read<int>();
                int modControl = mr.Read<int>();
                string modControlSha = mr.Read<string>();
                int gameMode = mr.Read<int>();
                bool cheats = mr.Read<bool>();
                int warpMode = mr.Read<int>();
                long universeSize = mr.Read<long>();
                string banner = mr.Read<string>();
                string homepage = mr.Read<string>();
                int httpPort = mr.Read<int>();
                string admin = mr.Read<string>();
                string team = mr.Read<string>();
                string location = mr.Read<string>();
                bool fixedIP = mr.Read<bool>();
                string[] players = mr.Read<string[]>();
                //Check if this is a new server
                client.serverHash = serverHash;
                Dictionary<string, object> hashParameters = new Dictionary<string, object>();
                hashParameters["@hash"] = serverHash;

                //Initialize if needed
                if (!client.initialized)
                {
                    client.lastReport = messageData;
                    client.initialized = true;
                    MainClass.DisconnectOtherClientsWithHash(client, serverHash);
                    string sqlQuery = "CALL gameserverinit(@serverhash, @namex, @descriptionx, @gameportx, @gameaddressx, @protocolx, @programversion, @maxplayersx, @modcontrolx, @modcontrolshax, @gamemodex, @cheatsx, @warpmodex, @universex, @bannerx, @homepagex, @httpportx, @adminx, @teamx, @locationx, @fixedipx);";
                    Dictionary<string, object> parameters = new Dictionary<string, object>();
                    parameters["@serverhash"] = serverHash;
                    parameters["@namex"] = serverName;
                    if (serverName.Length > 255)
                    {
                        serverName = serverName.Substring(0, 255);
                    }
                    parameters["@descriptionx"] = description;
                    parameters["@gameportx"] = gamePort;
                    parameters["@gameaddressx"] = gameAddress;
                    parameters["@protocolx"] = protocolVersion;
                    parameters["@programversion"] = programVersion;
                    parameters["@maxplayersx"] = maxPlayers;
                    parameters["@modcontrolx"] = modControl;
                    parameters["@modcontrolshax"] = modControlSha;
                    parameters["@gamemodex"] = gameMode;
                    parameters["@cheatsx"] = cheats;
                    parameters["@warpmodex"] = warpMode;
                    parameters["@universex"] = universeSize;
                    parameters["@bannerx"] = banner;
                    parameters["@homepagex"] = homepage;
                    parameters["@httpportx"] = httpPort;
                    parameters["@adminx"] = admin;
                    parameters["@teamx"] = team;
                    parameters["@locationx"] = location;
                    parameters["@fixedipx"] = fixedIP;
                    Console.WriteLine("Server " + serverHash + " is online!");
                    try
                    {
                        databaseConnection.ExecuteNonReader(sqlQuery, parameters);
                    }
                    catch
                    {
                        int thisID = Interlocked.Increment(ref reportID);
                        string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                        string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                        string errorBin = Path.Combine(errorPath, thisID + ".bin");
                        File.WriteAllBytes(errorBin, messageData);
                        using (StreamWriter errorFile = new StreamWriter(errorParameters))
                        {
                            errorFile.WriteLine("Reporting version 2");
                            errorFile.WriteLine("State: Init");
                            errorFile.WriteLine("SQL: " + sqlQuery);
                            foreach (KeyValuePair<string,object> kvp in parameters)
                            {
                                errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                            }
                        }
                        throw;
                    }
                }

                if (client.connectedPlayers == null)
                {
                    //Report connected players as connected
                    foreach (string connectedPlayer in players)
                    {
                        Console.WriteLine("Player " + connectedPlayer + " joined " + serverHash);
                        Dictionary<string, object> playerParams = new Dictionary<string, object>();
                        playerParams["@hash"] = serverHash;
                        playerParams["@player"] = connectedPlayer;
                        string sqlQuery = "CALL gameserverplayer(@hash, @player, '1')";
                        try
                        {
                            databaseConnection.ExecuteNonReader(sqlQuery, playerParams);
                        }
                        catch
                        {
                            int thisID = Interlocked.Increment(ref reportID);
                            string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                            string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                            string errorBin = Path.Combine(errorPath, thisID + ".bin");
                            File.WriteAllBytes(errorBin, messageData);
                            using (StreamWriter errorFile = new StreamWriter(errorParameters))
                            {
                                errorFile.WriteLine("Reporting version 2");
                                errorFile.WriteLine("State: Init Players");
                                errorFile.WriteLine("SQL: " + sqlQuery);
                                foreach (KeyValuePair<string,object> kvp in playerParams)
                                {
                                    errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                                }
                            }
                            throw;
                        }
                    }
                }
                else
                {
                    foreach (string player in players)
                    {
                        Console.WriteLine("Player: " + player);
                    }
                    //Take all the currently connected players and remove the players that were connected already to generate a list of players to be added
                    List<string> addList = new List<string>(players);
                    foreach (string player in client.connectedPlayers)
                    {
                        if (addList.Contains(player))
                        {
                            addList.Remove(player);
                        }
                    }
                    //Take all the old players connected and remove the players that are connected already to generate a list of players to be removed
                    List<string> removeList = new List<string>(client.connectedPlayers);
                    foreach (string player in players)
                    {
                        if (removeList.Contains(player))
                        {
                            removeList.Remove(player);
                        }
                    }
                    //Add new players
                    foreach (string player in addList)
                    {
                        Console.WriteLine("Player " + player + " joined " + serverHash);
                        Dictionary<string, object> playerParams = new Dictionary<string, object>();
                        playerParams["hash"] = serverHash;
                        playerParams["player"] = player;
                        string sqlQuery = "CALL gameserverplayer(@hash ,@player, '1')";
                        try
                        {
                            databaseConnection.ExecuteNonReader(sqlQuery, playerParams);
                        }
                        catch
                        {
                            int thisID = Interlocked.Increment(ref reportID);
                            string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                            string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                            string errorBin = Path.Combine(errorPath, thisID + ".bin");
                            File.WriteAllBytes(errorBin, messageData);
                            using (StreamWriter errorFile = new StreamWriter(errorParameters))
                            {
                                errorFile.WriteLine("Reporting version 2");
                                errorFile.WriteLine("State: Add Player");
                                errorFile.WriteLine("SQL: " + sqlQuery);
                                foreach (KeyValuePair<string,object> kvp in playerParams)
                                {
                                    errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                                }
                            }
                            throw;
                        }
                    }
                    //Remove old players
                    foreach (string player in removeList)
                    {
                        Console.WriteLine("Player " + player + " left " + serverHash);
                        Dictionary<string, object> playerParams = new Dictionary<string, object>();
                        playerParams["hash"] = serverHash;
                        playerParams["player"] = player;
                        string sqlQuery = "CALL gameserverplayer(@hash ,@player, '0')";
                        try
                        {
                            databaseConnection.ExecuteNonReader(sqlQuery, playerParams);
                        }
                        catch
                        {
                            int thisID = Interlocked.Increment(ref reportID);
                            string errorPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "errors");
                            string errorParameters = Path.Combine(errorPath, thisID + ".txt");
                            string errorBin = Path.Combine(errorPath, thisID + ".bin");
                            File.WriteAllBytes(errorBin, messageData);
                            using (StreamWriter errorFile = new StreamWriter(errorParameters))
                            {
                                errorFile.WriteLine("Reporting version 2");
                                errorFile.WriteLine("State: Remove Player");
                                errorFile.WriteLine("SQL: " + sqlQuery);
                                foreach (KeyValuePair<string,object> kvp in playerParams)
                                {
                                    errorFile.WriteLine(kvp.Key + " : " + kvp.Value.ToString());
                                }
                            }
                            throw;
                        }
                    }
                }
                //Save connected players for tracking
                client.connectedPlayers = players;

                sw.Stop();
                Console.WriteLine("Handled report from " + serverName + " (" + client.address + "), Protocol " + protocolVersion + ", Program Version: " + programVersion + ", Time: " + sw.ElapsedMilliseconds);
            }
        }
    }
}

