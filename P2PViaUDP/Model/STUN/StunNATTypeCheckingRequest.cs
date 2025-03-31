using System.Net;
using TURNServer;

namespace P2PViaUDP.Model.STUN;

public class StunNATTypeCheckingRequest
{
	#region 枚举定义

	/// <summary>
	/// 检查的类型
	/// </summary>
	public enum SubCheckingTypeEnum
	{
		//是否为对称型的判断
		IsSymmetric = 1,
		//锥形类型判断(在判断完了不是对称型以后再执行的)
		WhichKindOfCone = 2
	}

	#endregion
	public StunNATTypeCheckingRequest(
		Guid requestId, 
		SubCheckingTypeEnum subCheckingType,
		Guid clientId, 
		IPEndPoint toSTUNServerEndPoint, 
		DateTime sendTime)
	{
		RequestId = requestId;
		SubCheckingType = subCheckingType;
		ClientId = clientId;
		ToSTUNServerEndPoint = toSTUNServerEndPoint;
		SendTime = sendTime;
	}
	private static MessageType MessageType { get; } = MessageType.StunNATTypeCheckingRequest;
	private static ushort DefaultContentLength
		=> 0;//TBD
	/// <summary>
	/// 客户端发送该请求时分配的请求的Id
	/// </summary>
	public Guid RequestId { get; init; }
	/// <summary>
	/// 检查的类型,是1个包发给4个目标,通过出网是不是不同的公网端点来确认是不是对称型
	/// 或者是发给主服务器的主端口一份,让主服务器通知从服务器由2个IP4个端口返回来确定全锥形/端口受限锥/对称型
	/// </summary>
	public SubCheckingTypeEnum SubCheckingType { get; init; }
	public Guid ClientId { get; init; }
	public IPEndPoint ToSTUNServerEndPoint { get; set; }
	public DateTime SendTime { get; init; }
	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(RequestId.ToByteArray());
		bytesList.AddRange(BitConverter.GetBytes((int)SubCheckingType));
		bytesList.AddRange(ClientId.ToByteArray());
		bytesList.AddRange(ToSTUNServerEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(ToSTUNServerEndPoint.Port));
		bytesList.AddRange(BitConverter.GetBytes(SendTime.Ticks));
		var bytes = bytesList.ToArray();
		return bytes;
	}
	public static StunNATTypeCheckingRequest FromBytes(byte[] bytes)
	{
		if (bytes.Length < DefaultContentLength)
		{
			throw new ArgumentException($"解析StunNATTypeCheckingRequest字节数组长度不正确,应为{DefaultContentLength},实际为{bytes.Length}");
		}
		if ((MessageType)BitConverter.ToInt32(bytes, 0) != MessageType)
		{
			throw new ArgumentException("消息类型不正确");
		}
		var requestId = new Guid(bytes[4..20]);
		var subCheckingType = (SubCheckingTypeEnum)BitConverter.ToInt32(bytes, 20);
		var clientId = new Guid(bytes[24..40]);
		var toSTUNServerEndPoint = new IPEndPoint(new IPAddress(bytes[40..44]), BitConverter.ToInt32(bytes, 44));
		var sendTime = new DateTime(BitConverter.ToInt64(bytes, 48));
		return new StunNATTypeCheckingRequest(requestId, subCheckingType, clientId, toSTUNServerEndPoint, sendTime);
	}
}