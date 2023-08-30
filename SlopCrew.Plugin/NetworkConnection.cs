using System;
using System.Threading.Tasks;
using SlopCrew.Common;
using SlopCrew.Common.Network;
using SlopCrew.Common.Network.Serverbound;
using WebSocketSharp;
using WebSocket = WebSocketSharp.WebSocket;

namespace SlopCrew.Plugin;

public class NetworkConnection {
    [Flags]
    private enum SslProtocolsHack {
        Tls = 192,
        Tls11 = 768,
        Tls12 = 3072
    }

    public event Action<NetworkPacket>? OnMessageReceived;

    private WebSocket socket;

    public NetworkConnection() {
        var sslProtocolHack =
            (System.Security.Authentication.SslProtocols) (SslProtocolsHack.Tls12
                                                           | SslProtocolsHack.Tls11
                                                           | SslProtocolsHack.Tls);

        var addr = Plugin.SlopConfig.Address.Value;
        this.socket = new WebSocket(addr);
        if (addr.StartsWith("wss")) {
            this.socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;
        }

        SubscribeToSocketEvents();
        this.socket.EnableRedirection = true;
        this.socket.Connect();
    }

    private void SubscribeToSocketEvents() {
        this.socket.OnOpen += OnSocketOpen;
        this.socket.OnMessage += OnSocketMessage;
        this.socket.OnClose += OnSocketClose;
        this.socket.OnError += OnSocketError;
    }

    private void OnSocketOpen(object? sender, EventArgs e) {
        Plugin.API.UpdateConnected(true);

        this.SendMessage(new ServerboundVersion {
            Version = Constants.NetworkVersion
        });

        if (Plugin.PlayerManager is not null) {
            Plugin.PlayerManager.IsHelloRefreshQueued = true;
        }
    }

    private void OnSocketMessage(object? sender, MessageEventArgs args) {
        var packet = NetworkPacket.Read(args.RawData);
        OnMessageReceived?.Invoke(packet);
    }

    private void OnSocketClose(object? sender, CloseEventArgs e) {
        Plugin.API.UpdateConnected(false);
        Plugin.PlayerManager.IsResetQueued = true;
        Plugin.Log.LogInfo("Disconnected - reconnecting in 5s...");
        Task.Delay(5000).ContinueWith(_ => this.socket.Connect());
    }

    private void OnSocketError(object? sender, ErrorEventArgs e) {
        Plugin.Log.LogError($"WebSocket error: {e.Message}");
        // Handle or recover from the error
    }

    public void SendMessage(NetworkPacket packet) {
        var serialized = packet.Serialize();
        this.socket.Send(serialized);
    }

    public void Dispose() {
        UnsubscribeFromSocketEvents();
        this.socket.Close();
    }

    private void UnsubscribeFromSocketEvents() {
        this.socket.OnOpen -= OnSocketOpen;
        this.socket.OnMessage -= OnSocketMessage;
        this.socket.OnClose -= OnSocketClose;
        this.socket.OnError -= OnSocketError;
    }
}
