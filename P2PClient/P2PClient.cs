using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using P2PViaUDP;
using P2PViaUDP.Model;
using P2PViaUDP.Model.Client;
using P2PViaUDP.Model.TURN;

namespace P2PClient;

/// <summary>
/// 跟我相互打洞的客户端类
/// </summary>
public partial class PeerClient
{
    public PeerClient(IPEndPoint endPoint)
    {
        EndPoint = endPoint;
    }

    /// <summary>
    /// 客户端的Guid
    /// </summary>
    public Guid Guid { get; set; }
    /// <summary>
    /// 他的公网信息
    /// </summary>
    public IPEndPoint EndPoint { get; set; }
    /// <summary>
    /// 最后一次我发给他的心跳时间,如果还没发过则为null
    /// </summary>
    public DateTime? LastHeartbeatToHim { get; set; }
    /// <summary>
    /// 最后一次他发给我的心跳时间,如果还没收到过则为null
    /// </summary>
    public DateTime? LastHeartbeatFromHim { get; set; }
    /// <summary>
    /// 最后一次我收到他的时间,如果还没收到过则为null
    /// </summary>
    public DateTime? LastReceiveTime { get; set; }
    /// <summary>
    /// 最后一次我发送给他的时间,如果还没发送过则为null
    /// </summary>
    public DateTime? LastSendTime { get; set; }
    /// <summary>
    /// 首次P2P可用时间,我给他发过消息并且他也给我回过心跳的第一次设置一整个时间
    /// </summary>
    public DateTime? FirstP2PAvailableTime { get; set; }
}

public partial class PeerClient
{
    /// <summary>
    /// 判断是否已经建立了P2P
    /// </summary>
    public bool IsP2PHasBeenEstablished => FirstP2PAvailableTime != null;
}
public class P2PClient
{
    /// <summary>
    /// 跟我打洞的客户端集合,key是对方的Guid,value是对方的信息以及和我的相关交互信息
    /// </summary>
    private Dictionary<Guid, PeerClient> _peerClients = new();
    private readonly UdpClient _udpClient = new();
    private readonly Settings _settings = new();
    private IPEndPoint? _myEndPointFromStunReply;
    private readonly Guid _clientId = Guid.NewGuid();
    private bool _isRunning;

    public async Task StartAsync()
    {
        _isRunning = true;

        #region 如果是编译器附加的时候,则设置STUNServerIP为本地IP

        if (Debugger.IsAttached)
        {
            Console.WriteLine("调试模式已启用,将STUN服务器IP设置为本地IP");
            _settings.STUNServerIP = "127.0.0.1";
            Console.WriteLine($"我的ID: {_clientId}");
        }

        Console.WriteLine($"STUN服务器IP: {_settings.STUNServerIP}");

        #endregion
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
    
    public void Stop()
    {
        _isRunning = false;
        _udpClient.Close();
    }

    private async Task RequestStunServerAsync()
    {
        // 如果IP设置的不是IP的格式(域名)要解析成IP
        var domain = _settings.STUNServerIP;
        if (!IPAddress.TryParse(domain, out var _))
        {
            var ip = await Dns.GetHostAddressesAsync(domain);
            _settings.STUNServerIP = ip[0].ToString();
        }
        var serverEndPoint = new IPEndPoint(
            IPAddress.Parse(_settings.STUNServerIP),
            _settings.STUNServerPort
        );

        var stunRequest = new StunMessage(
            MessageType.StunRequest,
            MessageSource.Client,
            _clientId,
            serverEndPoint
        );

        var requestBytes = stunRequest.ToBytes();
        await _udpClient.SendAsync(requestBytes, requestBytes.Length, serverEndPoint);

        var receiveResult = await _udpClient.ReceiveAsync();
        var response = StunMessage.FromBytes(receiveResult.Buffer);
        
        if (response.MessageType == MessageType.StunResponse)
        {
            _myEndPointFromStunReply = response.ClientEndPoint;
            Console.WriteLine($"STUN 响应: 公网终端点 {_myEndPointFromStunReply}");
        }

        #region 每隔50MS(暂定)向额外STUN端口请求进行连接以供STUN能抓到本机的公网IP和端口变化规律

        //注意IP可能确实是不同的,因为我的ID不变但是出网可能因为双线光纤之类的自动切换
        foreach (var additionalPort in _settings.STUNServerAdditionalPorts)
        {
            var additionalServerEndPoint = new IPEndPoint(
                IPAddress.Parse(_settings.STUNServerIP),
                additionalPort
            );

            var additionalStunRequest = new StunMessage(
                MessageType.StunRequest,
                MessageSource.Client,
                _clientId,
                additionalServerEndPoint
            );

            var additionalRequestBytes = additionalStunRequest.ToBytes();
            await _udpClient.SendAsync(additionalRequestBytes, additionalRequestBytes.Length, additionalServerEndPoint);
            Console.WriteLine($"已发送额外STUN请求到: {additionalServerEndPoint}");
        }

        #endregion
    }

    private async Task RegisterToTurnServerAsync()
    {
        try
        {
            //如果配置的TURN服务器IP不是IP格式的话要解析成IP
            var domain = _settings.TURNServerIP;
            if (!IPAddress.TryParse(domain, out var _))
            {
                var ip = await Dns.GetHostAddressesAsync(domain);
                _settings.TURNServerIP = ip[0].ToString();
            }
            if (_myEndPointFromStunReply == null)
            {
                throw new Exception("STUN响应为空");
            }
            var registerMessage = new TURNRegisterMessage
            {
                EndPoint = _myEndPointFromStunReply,
                Guid = _clientId,
                GroupGuid = Guid.Parse("00000000-0000-0000-0000-000000000001")
            };

            var turnServerEndPoint = new IPEndPoint(
                IPAddress.Parse(_settings.TURNServerIP),
                _settings.TURNServerPort
            );

            Console.WriteLine($"正在向TURN服务器注册: {turnServerEndPoint}");
            Console.WriteLine($"本地终端点: {_myEndPointFromStunReply}");
        
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
        while (_isRunning)
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
        Console.WriteLine($"收到消息，大小: {data.Length}, 内容: {BitConverter.ToString(data)}");
        var messageType = (MessageType)data[0];
        switch (messageType)
        {
            case MessageType.TURNBroadcast:
                await ProcessBroadcastMessageAsync(data);
                break;
            case MessageType.P2PHolePunchingRequest:
                await ProcessP2PHolePunchingMessageAsync(data);
                break;
            case MessageType.P2PHeartbeat:
                await ProcessP2PHeartbeatMessageAsync(data);
                break;
            case MessageType.StunRequest:
            case MessageType.StunResponse:
            case MessageType.StunResponseError:
            case MessageType.TURNRegister:
            case MessageType.TURNServer2ClientHeartbeat:
            case MessageType.TURNClient2ServerHeartbeat:
            case MessageType.P2PHolePunchingResponse:
            default:
                Console.WriteLine($"未知消息类型: {messageType}");
                break;
        }
    }

    private Task ProcessP2PHeartbeatMessageAsync(byte[] data)
    {
        try
        {
            // 从字节数组中解析P2P心跳消息
            var heartbeatMessage = P2PHeartbeatMessage.FromBytes(data);
            Console.WriteLine($"收到P2P心跳消息，来自: {heartbeatMessage.SenderId}");
            // 更新对方的心跳时间
            if (_peerClients.TryGetValue(heartbeatMessage.SenderId, out var peer))
            {
                peer.LastHeartbeatFromHim = DateTime.Now;
                Console.WriteLine($"已更新对方的心跳时间: {heartbeatMessage.SenderId}");
            }
            else
            {
                Console.WriteLine($"未找到对方的信息: {heartbeatMessage.SenderId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理P2P心跳消息时出错: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    private Task ProcessP2PHolePunchingMessageAsync(byte[] data)
    {
        try
        {
            // 从字节数组中解析P2P打洞消息
            var holePunchingMessageFromOtherClient = Client2ClientP2PHolePunchingRequestMessage.FromBytes(data);
            Console.WriteLine($"收到P2P打洞消息，来自: {holePunchingMessageFromOtherClient.SourceEndPoint}");
            // 他要跟我打洞,我看我这边记录没有记录他的信息,如果没记录则记录一下,如果记录了则更新他的端点的相关信息
            var peerId = holePunchingMessageFromOtherClient.SourceClientId;
            if (!_peerClients.TryGetValue(peerId, out var peer))
            {
                _peerClients.Add(peerId, new PeerClient(holePunchingMessageFromOtherClient.SourceEndPoint)
                {
                    Guid = peerId
                });
                Console.WriteLine($"新的PeerClient已加入: {peerId}");
            }
            else
            {
                peer.EndPoint = holePunchingMessageFromOtherClient.SourceEndPoint;
            }
            if (_myEndPointFromStunReply == null)
            {
                throw new Exception("STUN响应为空, 无法处理P2P打洞消息");
            }
            // 然后我开启一个新的线程去给她发送我的心跳包给他
            ContinuousSendP2PHeartbeatMessagesAsync(holePunchingMessageFromOtherClient);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理P2P打洞消息时出错: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    private void ContinuousSendP2PHeartbeatMessagesAsync(
        Client2ClientP2PHolePunchingRequestMessage holePunchingMessageFromOtherClient)
    {
        Task.Run(async () =>
        {
            Console.WriteLine("开始发送P2P打洞消息");
            var sentTimes = 0;
            while (_isRunning)
            {
                sentTimes++;
                if (sentTimes > 2000)
                {
                    Console.WriteLine("已发送3次心跳包，停止发送");
                    break;
                }

                var heartbeatMessage = new P2PHeartbeatMessage(_clientId, $"NORMAN P2P HEARTBEAT {sentTimes}");
                //发送
                var heartbeatBytes = heartbeatMessage.ToBytes();
                await _udpClient.SendAsync(heartbeatBytes, heartbeatBytes.Length, holePunchingMessageFromOtherClient.SourceEndPoint);
                Console.WriteLine($"已发送心跳包到: {holePunchingMessageFromOtherClient.SourceEndPoint}, 第{sentTimes}次");
                //延迟2秒继续发
                await Task.Delay(2000);
            }
        });
    }

    private async Task ProcessBroadcastMessageAsync(byte[] data)
    {
        if (_myEndPointFromStunReply == null)
        {
            throw new Exception("STUN响应为空, 无法处理广播消息");
        }
        try
        {
            // 从字节数组中解析广播消息
            var broadcastMessage = TURNBroadcastMessage.FromBytes(data);
            Console.WriteLine($"收到广播消息，来自: {broadcastMessage.EndPoint}");
            if (broadcastMessage.Guid == _clientId)
            {
                Console.WriteLine("收到自己的广播消息，忽略");
                return;
            }
            var holePunchingMessage = new Client2ClientP2PHolePunchingRequestMessage
            {
                SourceEndPoint = _myEndPointFromStunReply,
                DestinationEndPoint = broadcastMessage.EndPoint, DestinationClientId = broadcastMessage.Guid
                , SourceClientId = _clientId, GroupId = broadcastMessage.GroupGuid, SendTime = DateTime.Now
            };
            
            //加入到对方的PeerClient集合
            if (!_peerClients.TryGetValue(broadcastMessage.Guid, out var peer))
            {
                _peerClients.Add(broadcastMessage.Guid, new PeerClient(broadcastMessage.EndPoint)
                {
                    Guid = broadcastMessage.Guid
                });
                Console.WriteLine($"新的PeerClient已加入: {broadcastMessage.Guid}");
            }
            else
            {
                peer.EndPoint = broadcastMessage.EndPoint;
            }

            // 处理P2P打洞
            await SendHolePunchingMessageAsync(holePunchingMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理广播消息时出错: {ex.Message}");
            throw;
        }
    }

    private async Task SendHolePunchingMessageAsync(Client2ClientP2PHolePunchingRequestMessage message)
    {
        if (_myEndPointFromStunReply == null)
        {
            throw new Exception("STUN响应为空, 无法发送P2P打洞消息");
        }

        const int maxRetries = 2;
        const int retryDelay = 1000;

        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var messageBytes = message.ToBytes();
                await _udpClient.SendAsync(messageBytes, messageBytes.Length, message.DestinationEndPoint);
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