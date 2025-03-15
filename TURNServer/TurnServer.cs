// 修改TURN服务器代码

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using P2PViaUDP.Model;
using P2PViaUDP.Model.TURN;

namespace TURNServer;

public class TurnServer
{
    private readonly UdpClient _udpServer;
    private readonly ConcurrentDictionary<Guid, List<TURNClient>> _groupDict;
    private readonly Settings _settings;

    public TurnServer(Settings settings)
    {
        _settings = settings;
        _groupDict = new ConcurrentDictionary<Guid, List<TURNClient>>();
        // 只指定端口，监听所有IP
        _udpServer = new UdpClient(settings.TURNServerPort);
        
        // 添加测试组
        _groupDict.TryAdd(Guid.Parse("00000000-0000-0000-0000-000000000001"), 
            new List<TURNClient>());
    }

    public async Task StartAsync()
    {
        Console.WriteLine($"TURN服务器启动在端口: {_settings.TURNServerPort}");
        
        while (true)
        {
            try
            {
                var result = await _udpServer.ReceiveAsync();
                ProcessMessage(result.Buffer, result.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TURN服务器错误: {ex.Message}");
            }
        }
    }

    private void ProcessMessage(byte[] data, IPEndPoint remoteEndPoint)
    {
        try
        {
            Console.WriteLine($"收到数据 来自: {remoteEndPoint}");
            Console.WriteLine($"数据长度: {data.Length}");
            Console.WriteLine($"原始数据: {BitConverter.ToString(data)}");

            var message = TURNRegisterMessage.FromBytes(data);
            
            Console.WriteLine($"解析后消息:");
            Console.WriteLine($"Guid: {message.Guid}");
            Console.WriteLine($"EndPoint: {message.EndPoint}");
            Console.WriteLine($"GroupGuid: {message.GroupGuid}");

            if (_groupDict.TryGetValue(message.GroupGuid, out var group))
            {
                group.Add(new TURNClient
                {
                    EndPointFromTURN = message.EndPoint,
                    Guid = message.Guid
                });
                
                Console.WriteLine($"客户端 {message.Guid} 已加入组 {message.GroupGuid}");
                BroadcastToGroup(message, group);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理消息时出错: {ex}");
        }
    }

    private void BroadcastToGroup(TURNRegisterMessage newClient, List<TURNClient> group)
    {
        foreach (var client in group.Where(c => c.Guid != newClient.Guid))
        {
            try
            {
                var broadcast = new TURNBroadcastMessage
                {
                    EndPoint = newClient.EndPoint,
                    Guid = newClient.Guid,
                    GroupGuid = newClient.GroupGuid
                };

                var data = broadcast.ToBytes();
                _udpServer.Send(data, data.Length, client.EndPointFromTURN);
                Console.WriteLine($"广播已发送到 {client.Guid}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"广播失败: {ex.Message}");
            }
        }
    }
}