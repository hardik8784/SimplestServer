using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    LinkedList<PlayerAccount> PlayerAccounts;

    const int PlayerAccountNameAndPassword = 1;

    string PlayerAccountFilePath;

    int PlayerWaitingForMatch = -1;

    LinkedList<GameSession> GameSessions;

    //int TotalTurnCount = 0;

    private int[,] ticTacToeServerBoard;

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        ticTacToeServerBoard = new int[3, 3];

        PlayerAccounts = new LinkedList<PlayerAccount>();
        

        GameSessions = new LinkedList<GameSession>();

        PlayerAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccountData.txt";

        LoadPlayerAccounts();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }
  
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }
    
    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');

        int signifier = int.Parse(csv[0]);

        #region Check Player has existing account or not, if not, create one
        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            string n = csv[1];
            string p = csv[2];
            bool nameInUse = false;
            bool isUnique = true;

            foreach (PlayerAccount pa in PlayerAccounts)
            {
                if (pa.name == n)
                {
                    isUnique = false;
                    break;
                }
            }
            if (isUnique)
            {
             
                PlayerAccount newPlayerAccount = new PlayerAccount(n, p);

                PlayerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "", id);

                SavePlayerAccounts();
            }
            else
            {
              
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "", id);
            }

        }
        #endregion

        #region If Player has already created account, see it matches with the Credentials, Do login after that
        else if (signifier == ClientToServerSignifiers.Login)
        {
            string n = csv[1];
            string p = csv[2];

            bool hasBeenFound = false;


            foreach (PlayerAccount pa in PlayerAccounts)
            {
                if (pa.name == n)
                {
                    if (pa.password == p)
                    {
                        Debug.Log("Login Account");
                        SendMessageToClient(ServerToClientSignifiers.LoginComplete + "," + n, id);
            
                    }
                    else
                    {
                        Debug.Log("Login Failed");

                        SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                  
                    }

                
                    hasBeenFound = true;
                    break;
                }
            }
            if (!hasBeenFound)
            {
                SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
            }

        }

        #endregion

        #region If both the Clients are waiting then add them to the GameRoom/GameSession
        else if (signifier == ClientToServerSignifiers.WaitingToJoinGameRoom)
        {

            if (PlayerWaitingForMatch == -1)
            {
                if (id <= 2)
                {
                    Debug.Log("We need to get this Player into a waiting queue");
                    PlayerWaitingForMatch = id; 
                }
            }
            else
            {

            
                if (id <= 2)
                {
                    GameSession gs = new GameSession(PlayerWaitingForMatch, id);
                    GameSessions.AddLast(gs);

              
                    SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gs.Players[0], gs.Players[0]);
                    SendMessageToClient(ServerToClientSignifiers.GameStart + "," + gs.Players[1], gs.Players[1]);

                    
                    SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + gs.Players[0], gs.Players[0]);
                    SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + gs.Players[0], gs.Players[1]);

             
                    PlayerWaitingForMatch = -1; 
                }

      
            }
        }
        #endregion
        else if (signifier == ClientToServerSignifiers.TicTacToe)
        {
        
            GameSession gs = FindGameSessionWithPlayerId(id);
            if (gs != null)
            {
                if (gs.Players[0] == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "", gs.Players[0]); 
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "", gs.Players[1]);
                }
            }

         

        }
        else if (signifier == ClientToServerSignifiers.PlayerAction)
        {
            GameSession gs = GetGameRoomWithClientID(id);
            if (gs != null)
            {
                int currentTurn;
            
                Debug.Log(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + "," + csv[2] + "," + csv[3]);

             
                ticTacToeServerBoard[int.Parse(csv[1]), int.Parse(csv[2])] = int.Parse(csv[3]);

                if (gs.Players[0] == id)
                {
                    currentTurn = gs.Players[1];
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + "," + csv[2] + "," + csv[3], gs.Players[1]);
                }
                else
                {
                    currentTurn = gs.Players[0];
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + "," + csv[2] + "," + csv[3], gs.Players[0]);
                }
               
                SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + currentTurn, gs.Players[0]);
                SendMessageToClient(ServerToClientSignifiers.ChangeTurn + "," + currentTurn, gs.Players[1]);

      
            }

    
        }
  
        else if (signifier == ClientToServerSignifiers.SendPresetMessage)
        {
            Debug.Log("Process Message: " + ClientToServerSignifiers.SendPresetMessage + "," + csv[1]);
            GameSession gs = GetGameRoomWithClientID(id);

            if (gs != null)
            {
                foreach (int Player in gs.Players)
                {
                    SendMessageToClient(ServerToClientSignifiers.SendMessage + "," + csv[1], Player);
                }
             
            }
        }
     
        else if (signifier == ClientToServerSignifiers.PlayerWins)
        {
            GameSession gs = GetGameRoomWithClientID(id);
            if (gs != null)
            {
              
                if (gs.Players[0] == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.NotifyOpponentWin + "," + id, gs.Players[1]);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.NotifyOpponentWin + "," + id, gs.Players[0]);
                }

              
            }
        }
    }


    #region Saving Player Account with StreamWriter
    private void SavePlayerAccounts()
    {
        StreamWriter sw = new StreamWriter(PlayerAccountFilePath);

        foreach (PlayerAccount pa in PlayerAccounts)
        {
            sw.WriteLine(pa.name + "," + pa.password);        
        }
        sw.Close();
    }
    #endregion

    private void LoadPlayerAccounts()
    {
        if (File.Exists(PlayerAccountFilePath))
        {
            StreamReader sr = new StreamReader(PlayerAccountFilePath);

            string Line;

            while ((Line = sr.ReadLine()) != null)
            {
                string[] csv = Line.Split(',');

                PlayerAccount pa = new PlayerAccount(csv[0], csv[1]);
                PlayerAccounts.AddLast(pa);
            }
        }
    }

    private GameSession FindGameSessionWithPlayerId(int id)
    {
        foreach(GameSession gs in GameSessions)
        {
            if(gs.Players[0] == id || gs.Players[1] == id)
            {
                return gs;
            }
        }
        return null;
    }

    private GameSession GetGameRoomWithClientID(int id)
    {
        foreach (GameSession gs in GameSessions)
        {
            if (gs.Players[0] == id || gs.Players[1] == id)
            {
                return gs;
            }
       
        }
        return null;
    }
}

public class PlayerAccount
{
    public string name,password;

    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}

public class GameSession
{
    public List<int> Players;
   
    public GameSession(int playerID1, int playerID2)
    {
        Players = new List<int>();
      
        Players.Add(playerID1);
        Players.Add(playerID2);

        // only need to worry about these two
        Debug.Log("Players " + Players[0] + "," + Players[1]);

    }

    public GameSession()
    {
        Players = new List<int>();

    }

  
}

public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int WaitingToJoinGameRoom = 3;
    public const int TicTacToe = 4;
    public const int PlayerAction = 5;
    public const int SendPresetMessage = 6;
    public const int PlayerWins = 7;
}
public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int OpponentPlay = 5; 
    public const int GameStart = 6;
    public const int SendMessage = 7;
    public const int NotifyOpponentWin = 8; 
    public const int ChangeTurn = 9;
    public const int GameReset = 10;
}