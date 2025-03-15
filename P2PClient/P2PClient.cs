using System.Net;
using System.Net.Sockets;
using P2PViaUDP.Model;
using P2PViaUDP.Model.Client;
using P2PViaUDP.Model.TURN;

public class P2PClient
{
    private readonly UdpClient _udpClient;
    private readonly Settings _settings;
    private IPEndPoint _myEndPoint;
    private readonly Guid _clientId;

    public P2PClient()
    {
        _settings = new Settings();
        _clientId = Guid.NewGuid();
        _udpClient = new UdpClient();
    }

    public async Task StartAsync()
    {
        try
        {
            // STUN 阶段
            await RequestStunServerAsync();
            
            // TURN 阶段
            await RegisterToTurnServerAsync();
            
            // 持续监听
            await StartListeningAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"发生错误: {ex.Message}");
        }
    }

    private async Task RequestStunServerAsync()
    {
        var serverEndPoint = new IPEndPoint(
            IPAddress.Parse(_settings.STUNServerIP),
            _settings.STUNServerPort
        );

        var stunRequest = new StunMessage(
            MessageType.StunRequest,
            MessageSource.Client,
            _clientId,
            new IPEndPoint(IPAddress.Any, 0),
            serverEndPoint
        );

        var requestBytes = stunRequest.ToBytes();
        await _udpClient.SendAsync(requestBytes, requestBytes.Length, serverEndPoint);

        var receiveResult = await _udpClient.ReceiveAsync();
        var response = StunMessage.FromBytes(receiveResult.Buffer);
        
        if (response.MessageType == MessageType.StunResponse)
        {
            _myEndPoint = response.ClientEndPoint;
            Console.WriteLine($"STUN 响应: 公网终端点 {_myEndPoint}");
        }
    }

    private async Task RegisterToTurnServerAsync()
    {
        try
        {
            var registerMessage = new TURNRegisterMessage
            {
                EndPoint = _myEndPoint,
                Guid = _clientId,
                GroupGuid = Guid.Parse("00000000-0000-0000-0000-000000000001")
            };

            var turnServerEndPoint = new IPEndPoint(
                IPAddress.Parse(_settings.TURNServerIP),
                _settings.TURNServerPort
            );

            Console.WriteLine($"正在向TURN服务器注册: {turnServerEndPoint}");
            Console.WriteLine($"本地终端点: {_myEndPoint}");
        
            var registerBytes = registerMessage.ToBytes();
            Console.WriteLine($"发送数据大小: {registerBytes.Length}");
        
            await _udpClient.SendAsync(registerBytes, registerBytes.Length, turnServerEndPoint);
            Console.WriteLine("TURN注册消息已发送");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TURN注册失败: {ex}");
            throw;
        }
    }

    private async Task StartListeningAsync()
    {
        while (true)
        {
            try
            {
                var receiveResult = await _udpClient.ReceiveAsync();
                await ProcessReceivedMessageAsync(receiveResult.Buffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"接收消息错误: {ex.Message}");
            }
        }
    }

    private async Task ProcessReceivedMessageAsync(byte[] data)
    {
        var broadcastMessage = TURNBroadcastMessage.FromBytes(data);
        Console.WriteLine($"收到广播消息，来自: {broadcastMessage.EndPoint}");
        
        // 处理P2P打洞
        await SendHolePunchingMessageAsync(broadcastMessage);
    }

    private async Task SendHolePunchingMessageAsync(TURNBroadcastMessage message)
    {
        var p2pMessage = new Client2ClientP2PHolePunchingMessage
        {
            SourceEndPoint = _myEndPoint,
            DestinationEndPoint = message.EndPoint,
            SourceClientId = _clientId,
            DestinationClientId = message.Guid,
            GroupId = message.GroupGuid
        };

        const int maxRetries = 2;
        const int retryDelay = 1000;

        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var messageBytes = p2pMessage.ToBytes();
                await _udpClient.SendAsync(messageBytes, messageBytes.Length, message.EndPoint);
                Console.WriteLine("P2P打洞消息已发送");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送失败 ({i + 1}/{maxRetries}): {ex.Message}");
                if (i < maxRetries - 1)
                    await Task.Delay(retryDelay);
            }
        }
    }
}