using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Timers;


namespace FaceTimeClone.Controllers
{
    public class WebSocketHandler
    {
        static public ConcurrentDictionary<Guid, ( WebSocket Socket, string? username )> _sockets = new();
        static public ConcurrentDictionary<string, Guid> userNameToGuid = new();


        public async Task HandleAsync(WebSocket webSocket)
        {

            var id = Guid.NewGuid();
            string socketsUserName = "";

            
            _sockets.TryAdd(id, (webSocket, null));
            var buffer = new byte[1024 * 4];


            try
            {
                int socketTest = 0;
                foreach (var socketDictItem in _sockets)
                {
                    if (socketDictItem.Value.Socket.State == WebSocketState.Open)
                    {
                        socketTest++;
                    }

                }
                System.Diagnostics.Debug.WriteLine($"totoal sockets: {_sockets.Count}");
                System.Diagnostics.Debug.WriteLine($"Open sockets: {socketTest}");


                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        System.Diagnostics.Debug.WriteLine($"Received message: {message}");
                        System.Diagnostics.Debug.WriteLine($"messages length: {message.Length}\n");

                        var messJson = JsonSerializer.Deserialize < Dictionary<string, object>>(message);

                        



                        if (IsSendMessage(message, out string recipementGUID, out string data))
                        {
                            System.Diagnostics.Debug.WriteLine($"recipementGUID: {recipementGUID}, data: {data}");

                            System.Diagnostics.Debug.WriteLine($"Broadcasting: {message}");
                            WebSocket recipementSocket = _sockets[userNameToGuid[recipementGUID]].Socket;
                            // TODO: fix indentation
                            if (recipementSocket.State == WebSocketState.Open)
                            {
                                await recipementSocket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                            
                        }
                        else if (IsRegisterMessage(message, out string userName))
                        {
                            System.Diagnostics.Debug.WriteLine($"userName: {userName}");


                            if(userNameToGuid.ContainsKey(userName) && _sockets[userNameToGuid[userName]].Socket.State == WebSocketState.Open)
                            {
                                    await webSocket.SendAsync(Encoding.UTF8.GetBytes("error:usernamenotavaible"), WebSocketMessageType.Text, true, CancellationToken.None);

                            }
                            else
                            {
                                if (userNameToGuid.ContainsKey(userName))
                                {
                                    _sockets.TryRemove(userNameToGuid[userName], out _);
                                }
                                userNameToGuid[userName] = id;
                            }

                            System.Diagnostics.Debug.WriteLine($"userName set");
                            _sockets[id] = (webSocket, userName);
                            socketsUserName = userName;
                            userNameToGuid[userName] = id;



                            var usernames = _sockets.Values
                                .Where(v => v.Socket != null
                                            && v.Socket.State == WebSocketState.Open
                                            && !string.IsNullOrWhiteSpace(v.username))
                                .Select(v => v.username)
                                .ToArray();


                            var obj = new {users = usernames};
                            string welcomeString = $"welcome:{JsonSerializer.Serialize(obj)}";
                            await webSocket.SendAsync(Encoding.UTF8.GetBytes(welcomeString), WebSocketMessageType.Text, true, CancellationToken.None);

                            await BroadcastAsync($"newuser:{userName}");

                        }
                        else if (message == "broadcast")
                        {
                            await BroadcastAsync(message);
                        }
                        else
                        {

                            // Echo back
                            var serverMsg = Encoding.UTF8.GetBytes($"Server echo: {message}");
                            await webSocket.SendAsync(
                                new ArraySegment<byte>(serverMsg, 0, serverMsg.Length),
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None);


                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _sockets.TryRemove(id, out var removedSocket);
                        userNameToGuid.TryRemove(socketsUserName, out var _);
                        await BroadcastAsync("$userLeft:");

                        
                        System.Diagnostics.Debug.WriteLine("Closing WebSocket...");
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closed by server",
                            CancellationToken.None);

                    }
                }
            }
            catch (WebSocketException err)
            {
                System.Diagnostics.Debug.WriteLine($"Socket Exception caught: {err.Message}");
                _sockets.TryRemove(id, out var removedSocket);
                userNameToGuid.TryRemove(socketsUserName, out var _);

                await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
                            "Closed by server",
                            CancellationToken.None);
            }
            catch (Exception err)
            {
                System.Diagnostics.Debug.WriteLine($"Exception caught: {err.Message}");
                _sockets.TryRemove(id, out var removedSocket);
                userNameToGuid.TryRemove(socketsUserName, out var _);
                await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
                    "Closed by server",
                    CancellationToken.None);
            }


        }

        private async Task BroadcastAsync(string message)
        {
            System.Diagnostics.Debug.WriteLine($"Broadcasting: {message}");
            foreach (var socketDictItem in _sockets)
            {
                if (socketDictItem.Value.Socket.State == WebSocketState.Open)
                {
                    await socketDictItem.Value.Socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
                }

                // might cause problems
                //else
                //{
                //    _sockets.TryRemove(socketDictItem.Key, out var removedSocket);
                //}
            }
        }

        static bool IsRegisterMessage(string input, out string userName)
        {
            const string FUNCITON_NAME = "register";
            userName = "";

            if (string.IsNullOrWhiteSpace(input))
                return false;

            string[] parts = input.Split(':');

            if (parts.Length == 2 && parts[0] == FUNCITON_NAME)
            {

                userName = parts[1];

                return true;
            }

            return false;
        }

        static bool IsSendMessage(string input, out string part1, out string part2)
        {
            const string FUNCTION_NAME = "send";
            part1 = "";
            part2 = "";

            if (string.IsNullOrWhiteSpace(input))
                return false;

            string[] parts = input.Split(':');

            if (parts.Length == 3 && parts[0] == FUNCTION_NAME)
            {
                part1 = parts[1];
                part2 = parts[2];
                return true;
            }

            return false;
        }
    }
}

