namespace P2PViaUDP.Model.TURN;

public class TURNCheckNATConsistencyRequest
{
    private static MessageType MessageType => MessageType.TURNCheckNATConsistencyRequest;

    private static uint DefaultMessageLength => 4 + // MessageType
                                                16; // Guid

    public Guid ClientId { get; init; }

    public static TURNCheckNATConsistencyRequest FromBytes(byte[] data)
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

        return new TURNCheckNATConsistencyRequest
        {
            ClientId = clientId,
        };
    }

    public byte[] ToBytes()
    {
        var bytesList = new List<byte>();
        bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
        bytesList.AddRange(ClientId.ToByteArray());
        return bytesList.ToArray();
    }
} 