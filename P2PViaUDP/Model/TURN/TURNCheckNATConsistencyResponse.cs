using System.Net;

namespace P2PViaUDP.Model.TURN;

public class TURNCheckNATConsistencyResponse
{
    private static MessageType MessageType => MessageType.TURNCheckNATConsistencyResponse;
    private static uint DefaultMessageLength => 4 + // MessageType
        16 + // Guid
        4 + // Address
        4; // Port

    public Guid ClientId { get; init; }
    public IPEndPoint EndPoint { get; init; }

    public static TURNCheckNATConsistencyResponse FromBytes(byte[] data)
    {
        if (data.Length != DefaultMessageLength)
        {
            throw new ArgumentException($"接收到的字节数组长度不正确，应为{DefaultMessageLength}，实际为{data.Length}");
        }

        var messageType = (MessageType)BitConverter.ToInt32(data, 0);
        if (messageType != MessageType && messageType != MessageType.TURNCheckNATConsistencyResponse)
        {
            throw new ArgumentException("读取的消息类型不匹配");
        }

        var clientId = new Guid(data.Skip(4).Take(16).ToArray());
        var address = new IPAddress(data.Skip(20).Take(4).ToArray());
        var port = BitConverter.ToInt32(data, 24);
        var endPoint = new IPEndPoint(address, port);

        return new TURNCheckNATConsistencyResponse
        {
            ClientId = clientId,
            EndPoint = endPoint
        };
    }

    public byte[] ToBytes()
    {
        var bytesList = new List<byte>();
        bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
        bytesList.AddRange(ClientId.ToByteArray());
        bytesList.AddRange(EndPoint.Address.GetAddressBytes());
        bytesList.AddRange(BitConverter.GetBytes(EndPoint.Port));
        return bytesList.ToArray();
    }
} 