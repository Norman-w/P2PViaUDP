using System.Net;
using TURNServer;

namespace P2PViaUDP.Model.Client;

/// <summary>
/// 客户端到客户端的P2P打洞消息
/// </summary>
public class Client2ClientP2PHolePunchingRequestMessage
{
	// 定义固定的消息长度组成
	private const int GuidLength = 16;  // Guid 长度
	private const int BoolLength = 1;   // 布尔值长度
	
	public Client2ClientP2PHolePunchingRequestMessage(
		Guid groupId,
		IPEndPoint destinationEndPoint, 
		Guid destinationClientId, 
		NATTypeEnum sourceNatType, 
		Guid sourceClientId, 
		IPEndPoint? sourceEndPoint = null
		)
	{
		DestinationEndPoint = destinationEndPoint;
		SourceNatType = sourceNatType;
		SourceClientId = sourceClientId;
		DestinationClientId = destinationClientId;
		GroupId = groupId;
		SourceEndPoint = sourceEndPoint;
		SendTime = DateTime.Now;
	}

	private static MessageType MessageType => MessageType.P2PHolePunchingRequest;
	// 计算消息长度
	public uint DefaultMessageLength
	{
		get
		{
			uint length = 0;
			length += sizeof(int);  // MessageType
			length += GuidLength * 4;  // 4个Guid
			length += BoolLength;  // SourceEndPoint是否存在的标志
			if (SourceEndPoint != null)
			{
				length += sizeof(int);  // 地址长度
				length += (uint)SourceEndPoint.Address.GetAddressBytes().Length;
				length += sizeof(int);  // 端口
			}
			length += sizeof(int);  // 目标地址长度
			length += (uint)DestinationEndPoint.Address.GetAddressBytes().Length;
			length += sizeof(int);  // 目标端口
			length += sizeof(int);  // NAT类型
			length += sizeof(long); // DateTime Ticks
			return length;
		}
	}
	
	public Guid RequestId { get; init; }
	
	/// <summary>
	/// 谁发起的打洞,并不是本条消息是谁发送的,这个不一定是有值的,因为对称型NAT在开始打洞的时候并不知道自己的端口信息
	/// 但是会知道自己的类型
	/// </summary>
	public IPEndPoint? SourceEndPoint { get; private init; }
	/// <summary>
	/// 谁接收打洞请求,并不是本条消息是谁接收的
	/// </summary>
	public IPEndPoint DestinationEndPoint { get; init; }
	/// <summary>
	/// 源NAT类型,如果是对称型的,则包到达目的地以后才知道源的出网时候的真正端口号.这种request需要修正了信息以后再response返回去
	/// </summary>
	public NATTypeEnum SourceNatType { get; init; }
	/// <summary>
	/// 源客户端ID,发起打洞的客户端ID
	/// </summary>
	public Guid SourceClientId { get; init; }
	/// <summary>
	/// 目标客户端ID,接收打洞请求的客户端ID
	/// </summary>
	public Guid DestinationClientId { get; init; }
	/// <summary>
	/// 组ID
	/// </summary>
	public Guid GroupId { get; init; }
	/// <summary>
	/// 发送时间
	/// </summary>
	public DateTime SendTime { get; init; }
	public byte[] ToBytes()
	{
		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);
		// 写入消息类型
		writer.Write((int)MessageType);
            
		// 写入各个Guid
		writer.Write(RequestId.ToByteArray());
		writer.Write(GroupId.ToByteArray());
		writer.Write(SourceClientId.ToByteArray());
		writer.Write(DestinationClientId.ToByteArray());

		// 处理SourceEndPoint（可能为null）
		writer.Write(SourceEndPoint != null);
		if (SourceEndPoint != null)
		{
			byte[] addressBytes = SourceEndPoint.Address.GetAddressBytes();
			writer.Write(addressBytes.Length);  // 写入地址长度
			writer.Write(addressBytes);
			writer.Write(SourceEndPoint.Port);
		}

		// 处理DestinationEndPoint
		var destAddressBytes = DestinationEndPoint.Address.GetAddressBytes();
		writer.Write(destAddressBytes.Length);  // 写入地址长度
		writer.Write(destAddressBytes);
		writer.Write(DestinationEndPoint.Port);

		// 写入NAT类型
		writer.Write((int)SourceNatType);

		// 写入发送时间
		writer.Write(SendTime.Ticks);

		return ms.ToArray();
	}

    public static Client2ClientP2PHolePunchingRequestMessage FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 16)  // 最小长度检查
        {
            throw new ArgumentException("无效的消息字节数组");
        }

        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);
        try
        {
	        // 读取消息类型
	        MessageType messageType = (MessageType)reader.ReadInt32();
	        if (messageType != MessageType.P2PHolePunchingRequest)
	        {
		        throw new ArgumentException("消息类型不匹配");
	        }

	        // 读取各个Guid
	        Guid requestId = new Guid(reader.ReadBytes(GuidLength));
	        Guid groupId = new Guid(reader.ReadBytes(GuidLength));
	        Guid sourceClientId = new Guid(reader.ReadBytes(GuidLength));
	        Guid destinationClientId = new Guid(reader.ReadBytes(GuidLength));

	        // 读取SourceEndPoint
	        IPEndPoint? sourceEndPoint = null;
	        bool hasSourceEndPoint = reader.ReadBoolean();
	        if (hasSourceEndPoint)
	        {
		        int addressLength = reader.ReadInt32();
		        byte[] addressBytes = reader.ReadBytes(addressLength);
		        int port = reader.ReadInt32();
		        sourceEndPoint = new IPEndPoint(new IPAddress(addressBytes), port);
	        }

	        // 读取DestinationEndPoint
	        int destAddressLength = reader.ReadInt32();
	        byte[] destAddressBytes = reader.ReadBytes(destAddressLength);
	        int destPort = reader.ReadInt32();
	        var destinationEndPoint = new IPEndPoint(new IPAddress(destAddressBytes), destPort);

	        // 读取NAT类型
	        NATTypeEnum sourceNatType = (NATTypeEnum)reader.ReadInt32();

	        // 读取发送时间
	        DateTime sendTime = new DateTime(reader.ReadInt64());

	        return new Client2ClientP2PHolePunchingRequestMessage(
		        groupId,
		        destinationEndPoint,
		        destinationClientId,
		        sourceNatType,
		        sourceClientId,
		        sourceEndPoint)
	        {
		        RequestId = requestId,
		        SendTime = sendTime
	        };
        }
        catch (EndOfStreamException)
        {
	        throw new ArgumentException("消息字节数组长度不足");
        }
    }
}