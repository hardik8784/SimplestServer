using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    LinkedList<PlayerAccount> PlayerAccounts;

    string PlayerAccountFilePath;

    int PlayerWaitingForMatch = -1;

    LinkedList<GameSession> GameSessions;

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

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

            bool isUnique = true;

            foreach(PlayerAccount pa in PlayerAccounts)
            {
                if(pa.name == n)
                {
                    isUnique = false;
                    break;
                }
            }
            if(isUnique)
            {
                PlayerAccounts.AddLast(new PlayerAccount(n,p));
                //SendMessageToClient(ServerToClientSignifiers.LoginResponse + "", id);
               SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.Success, id);

                SavePlayerAccounts();
            }
            else 
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureNameInUse, id);
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
                    if(pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.Success, id);
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.IncorrectPassword, id);
                    }

                    //Recognised the PlayerAccounts,Here to add
                    //SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.Success, id);
                    hasBeenFound = true;
                    break;
                }
            }
            if(!hasBeenFound)
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureNameNotFound, id);
            }
            
        }

        #endregion

        #region If both the Clients are waiting then add them to the GameRoom/GameSession
        else if (signifier == ClientToServerSignifiers.AddToGameSessionQueue)
        {

            //If there is none, save the player into above mentioned variable
            if (PlayerWaitingForMatch == -1)
            {
                //Make a Single Int Variable of the one and only waiting player
                PlayerWaitingForMatch = id;
            }
            else
            {
                //If there is awaiting player, Join...
                //Create a game session object, pass it to two players
                GameSession gs = new GameSession(PlayerWaitingForMatch, id);
                GameSessions.AddLast(gs);
                //pass a signifier to both the clients that they have joined one
                SendMessageToClient(ServerToClientSignifiers.GameSessionStarted + " ", id);
                SendMessageToClient(ServerToClientSignifiers.GameSessionStarted + " ", PlayerWaitingForMatch);

                PlayerWaitingForMatch = -1;
                Debug.Log("Player match done, Game Session Started");
            }
        }

        #endregion
        else if (signifier == ClientToServerSignifiers.TicTacToePlay)
        {
            Debug.Log("TicTacToePlay");

            GameSession gs = FindGameSessionWithPlayerId(id);

            if (gs.PlayerId1 == id)
            {
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + " ", gs.PlayerId2);
            }
            else
            {
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + " ", gs.PlayerId1);
            }
        }
        else if(signifier == ClientToServerSignifiers.OpponentTurn)
        {

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
            if(gs.PlayerId1 == id || gs.PlayerId2 == id)
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
    public int PlayerId1, PlayerId2;

    public GameSession(int Playerid1, int Playerid2)
    {
        PlayerId1 = Playerid1;
        PlayerId2 = Playerid2;
    }
}

public static class ClientToServerSignifiers
{
    public const int Login = 1;

    public const int CreateAccount = 2;

    public const int AddToGameSessionQueue = 3;

    public const int TicTacToePlay = 4;

    public const int OpponentTurn = 5;
}

public static class ServerToClientSignifiers
{
    public const int LoginResponse = 1;

    public const int GameSessionStarted = 2;

    public const int OpponentTicTacToePlay = 3;

    //public const int LoginFailure = 2;

    //public const int CreateAccountSuccess = 1;

    //public const int CreateAccountFailure = 2;
}

public static class LoginResponses
{
    public const int Success = 1;

    public const int FailureNameInUse = 2;

    public const int FailureNameNotFound = 3;

    public const int IncorrectPassword = 4;
}